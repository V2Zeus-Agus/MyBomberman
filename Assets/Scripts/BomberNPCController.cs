using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class BomberNPCController : MonoBehaviour
{
    private enum AIState { SeekPlayer, GoBombSpot, Escape }

    [Header("Refs")]
    public Transform player;
    public BombController bomb;                 // tu BombController
    public Grid grid;                     // el Grid de la escena
    public Tilemap destructibleTiles;           // mismos tiles destructibles
    public Tilemap walkableTiles; // tu WALKPATH


    [Header("Movimiento")]
    public float speed = 4f;
    [Tooltip("Tamaño de celda en mundo (si tu tile es 1x1, dejalo en 1).")]
    public float tileSize = 1f;

    [Header("Navegación (Bounds en coordenadas de celda)")]
    public RectInt gridBounds = new RectInt(-8, -5, 17, 11);

    [Header("Colisiones")]
    [Tooltip("Paredes/indestructibles/bombas. (Los destructibles se chequean por Tilemap).")]
    public LayerMask obstacleMask;
    public LayerMask explosionMask;

    [Header("IA")]
    public float replanEvery = 0.2f;
    public int searchRadiusTiles = 6;
    [Tooltip("Cuántas celdas considera para escapar (por seguridad).")]
    public int escapeSearchCap = 80;

    [Header("Sprites")]
    public AnimatedSpriteRenderer spriteRendererUp;
    public AnimatedSpriteRenderer spriteRendererDown;
    public AnimatedSpriteRenderer spriteRendererLeft;
    public AnimatedSpriteRenderer spriteRendererRight;
    private AnimatedSpriteRenderer activeSpriteRenderer;

    // ---- runtime ----
    public new Rigidbody2D rigidbody { get; private set; }
    private AIState state = AIState.SeekPlayer;

    private List<Vector3Int> currentPath = new List<Vector3Int>();

    private int pathIndex = 0;

    private Vector3Int targetCell;
    private Vector3Int chosenBombCell;      // donde va a plantar
    private Vector3Int chosenEscapeCell;    // donde se cubre
    private float nextReplanTime;

    private void Awake()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        activeSpriteRenderer = spriteRendererDown;
    }

    private void Start()
    {
        if (walkableTiles != null)
        {
            BoundsInt b = walkableTiles.cellBounds;
            gridBounds = new RectInt(b.xMin, b.yMin, b.size.x, b.size.y);
        }
    }


    private void Update()
    {
        if (!player || !bomb || !grid) return;
        //Debug.Log($"NPCcell={WorldToCell(transform.position)} PlayerCell={WorldToCell(player.position)} InBoundsNPC={gridBounds.Contains(new Vector2Int(WorldToCell(transform.position).x, WorldToCell(transform.position).y))} InBoundsP={gridBounds.Contains(new Vector2Int(WorldToCell(player.position).x, WorldToCell(player.position).y))}");

        // Si estoy parado en una explosion: escape ya
        if (IsInExplosionWorld(transform.position))
            state = AIState.Escape;

        if (Time.time >= nextReplanTime)
        {
            nextReplanTime = Time.time + replanEvery;
            ThinkAndPlan();
        }

        AnimateFromVelocity(rigidbody.velocity);
    }

    private void FixedUpdate()
    {
        FollowPlannedPath();
    }

    // ===================== IA CORE =====================

    private void ThinkAndPlan()
    {
        Vector3Int myCell = WorldToCell(transform.position);

        // 1) Si hay peligro inmediato o cercano: escapar
        if (IsCellDangerousNow(myCell))
        {
            state = AIState.Escape;
            PlanEscape(myCell);
            return;
        }

        // 2) Si ya estaba yendo a plantar y llegó:
        if (state == AIState.GoBombSpot && myCell == chosenBombCell)
        {
            // Plantar bomba (usa tu hook)
            bool placed = bomb.TryPlaceBomb();
            // Si no pudo (sin stock), vuelve a perseguir
            if (!placed)
            {
                state = AIState.SeekPlayer;
                PlanChase(myCell);
                return;
            }

            // Apenas planta: planear escape por la explosión futura
            state = AIState.Escape;
            PlanEscapeFromBombCell(myCell, myCell);
            return;
        }

        // 3) Si hay un spot de bomba bueno: ir ahí
        if (TryChooseBombSpot(myCell, out Vector3Int bombCell, out Vector3Int escapeCell))
        {
            chosenBombCell = bombCell;
            chosenEscapeCell = escapeCell;
            state = AIState.GoBombSpot;
            PlanPathTo(myCell, chosenBombCell);
            return;
        }

        // 4) Si no: perseguir player
        state = AIState.SeekPlayer;
        PlanChase(myCell);
    }

    private void PlanChase(Vector3Int myCell)
    {
        Vector3Int pCell = WorldToCell(player.position);

        // Si la celda del player no es walkable (o está “rara”), buscar una walkable cerca
        Vector3Int chaseTarget = GetNearestWalkableAround(pCell);
        PlanPathTo(myCell, chaseTarget);
    }

    private Vector3Int GetNearestWalkableAround(Vector3Int center)
    {
        if (IsWalkableCell(center)) return center;

        // vecinos directos primero (bomberman feel)
        foreach (var n in Neighbors4(center))
            if (IsWalkableCell(n)) return n;

        // fallback: BFS corto hasta encontrar walkable
        Queue<Vector3Int> q = new Queue<Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

        q.Enqueue(center);
        visited.Add(center);

        int guard = 0;
        while (q.Count > 0 && guard++ < 80)
        {
            var cur = q.Dequeue();
            foreach (var n in Neighbors4(cur))
            {
                if (visited.Contains(n)) continue;
                if (!gridBounds.Contains(new Vector2Int(n.x, n.y))) continue;

                visited.Add(n);

                if (IsWalkableCell(n)) return n;
                q.Enqueue(n);
            }
        }

        return center;
    }

    private void PlanEscape(Vector3Int myCell)
    {
        // escape por peligro actual (explosiones activas)
        if (TryFindNearestSafeCell(myCell, (c) => !IsCellDangerousNow(c), out Vector3Int safe))
        {
            PlanPathTo(myCell, safe);
        }
        else
        {
            // fallback: no encontró, frena
            ClearPath();
        }
    }

    private void PlanEscapeFromBombCell(Vector3Int myCell, Vector3Int bombCell)
    {
        // Escape usando predicción de explosión de bomba propia
        HashSet<Vector3Int> predicted = ComputePredictedBlastCells(bombCell, bomb.explosionRadius);

        bool IsSafe(Vector3Int c) => !predicted.Contains(c) && !IsCellDangerousNow(c);

        if (TryFindNearestSafeCell(myCell, IsSafe, out Vector3Int safe))
        {
            PlanPathTo(myCell, safe);
        }
        else
        {
            ClearPath();
        }
    }

    // ===================== BOMB SPOT =====================

    // “Extremo del ladrillo” = celda caminable adyacente a un tile destructible
    private bool TryChooseBombSpot(Vector3Int myCell, out Vector3Int bombCell, out Vector3Int escapeCell)
    {
        bombCell = default;
        escapeCell = default;

        if (!destructibleTiles) return false;

        Vector3Int bestBomb = default;
        Vector3Int bestEscape = default;
        int bestScore = int.MaxValue;

        // buscamos destructibles alrededor
        for (int dx = -searchRadiusTiles; dx <= searchRadiusTiles; dx++)
            for (int dy = -searchRadiusTiles; dy <= searchRadiusTiles; dy++)
            {
                Vector3Int dCell = new Vector3Int(myCell.x + dx, myCell.y + dy, 0);
                if (!gridBounds.Contains(new Vector2Int(dCell.x, dCell.y))) continue;

                if (!destructibleTiles.HasTile(dCell)) continue;

                // candidatos de colocación: vecinos caminables del destructible
                foreach (Vector3Int n in Neighbors4(dCell))
                {
                    if (!gridBounds.Contains(new Vector2Int(n.x, n.y))) continue;
                    if (!IsWalkableCell(n)) continue;

                    // tiene sentido plantar ahí solo si puedo llegar
                    if (!TryBFSPath(myCell, n, out List<Vector3Int> pathToBomb)) continue;

                    // y si al plantar puedo escapar
                    HashSet<Vector3Int> predicted = ComputePredictedBlastCells(n, bomb.explosionRadius);
                    bool IsSafe(Vector3Int c) => !predicted.Contains(c) && !IsCellDangerousNow(c);

                    if (!TryFindNearestSafeCell(n, IsSafe, out Vector3Int safe)) continue;

                    // score: distancia al spot (más cerca mejor)
                    int score = pathToBomb.Count;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestBomb = n;
                        bestEscape = safe;
                    }
                }
            }

        if (bestScore == int.MaxValue) return false;

        bombCell = bestBomb;
        escapeCell = bestEscape;
        return true;
    }

    // ===================== PATHFINDING (BFS) =====================

    private void PlanPathTo(Vector3Int from, Vector3Int to)
    {
        if (from == to)
        {
            ClearPath();
            return;
        }

        if (TryBFSPath(from, to, out List<Vector3Int> path))
        {
            currentPath = path;
            pathIndex = 0;
        }
        else
        {
            ClearPath();
        }
    }

    private bool TryBFSPath(Vector3Int start, Vector3Int goal, out List<Vector3Int> path)
    {
        path = null;

        if (!IsWalkableCell(goal)) return false;

        Queue<Vector3Int> q = new Queue<Vector3Int>();
        Dictionary<Vector3Int, Vector3Int> came = new Dictionary<Vector3Int, Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

        q.Enqueue(start);
        visited.Add(start);

        int guard = 0;

        while (q.Count > 0 && guard++ < 5000)
        {
            Vector3Int cur = q.Dequeue();
            if (cur == goal)
            {
                path = ReconstructPath(came, start, goal);
                return true;
            }

            foreach (var n in Neighbors4(cur))
            {
                if (!gridBounds.Contains(new Vector2Int(n.x, n.y))) continue;
                if (visited.Contains(n)) continue;
                if (!IsWalkableCell(n)) continue;

                visited.Add(n);
                came[n] = cur;
                q.Enqueue(n);
            }
        }

        return false;
    }

    private List<Vector3Int> ReconstructPath(Dictionary<Vector3Int, Vector3Int> came, Vector3Int start, Vector3Int goal)
    {
        List<Vector3Int> p = new List<Vector3Int>();
        Vector3Int cur = goal;
        p.Add(cur);

        while (cur != start && came.ContainsKey(cur))
        {
            cur = came[cur];
            p.Add(cur);
        }

        p.Reverse();
        return p;
    }

    private bool TryFindNearestSafeCell(Vector3Int from, System.Func<Vector3Int, bool> isSafe, out Vector3Int safe)
    {
        safe = default;

        Queue<Vector3Int> q = new Queue<Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

        q.Enqueue(from);
        visited.Add(from);

        int expanded = 0;

        while (q.Count > 0 && expanded++ < escapeSearchCap)
        {
            var cur = q.Dequeue();
            if (IsWalkableCell(cur) && isSafe(cur))
            {
                safe = cur;
                return true;
            }

            foreach (var n in Neighbors4(cur))
            {
                if (!gridBounds.Contains(new Vector2Int(n.x, n.y))) continue;
                if (visited.Contains(n)) continue;
                if (!IsWalkableCell(n)) continue;

                visited.Add(n);
                q.Enqueue(n);
            }
        }

        return false;
    }

    // ===================== MOVEMENT =====================

    private void FollowPlannedPath()
    {
        if (currentPath == null || currentPath.Count == 0 || pathIndex >= currentPath.Count)
        {
            rigidbody.velocity = Vector2.zero;
            return;
        }

        Vector3Int nextCell = currentPath[pathIndex];
        Vector2 nextWorld = CellToWorldCenter(nextCell);

        Vector2 pos = rigidbody.position;
        Vector2 to = nextWorld - pos;

        if (to.magnitude < 0.08f)
        {
            pathIndex++;
            rigidbody.velocity = Vector2.zero;
            return;
        }

        Vector2 dir = to.normalized;
        Vector2 translation = dir * speed * Time.fixedDeltaTime;

        rigidbody.MovePosition(pos + translation);
    }


    private void ClearPath()
    {
        currentPath.Clear();
        pathIndex = 0;
        rigidbody.velocity = Vector2.zero;

    }

    // ===================== DANGER / BLAST PREDICTION =====================

    private bool IsInExplosionWorld(Vector2 worldPos)
    {
        // explosión activa
        return Physics2D.OverlapBox(worldPos, Vector2.one * 0.45f, 0f, explosionMask) != null;
    }

    private bool IsCellDangerousNow(Vector3Int cell)
    {
        // peligro actual por explosiones activas (simple y confiable)
        return IsInExplosionWorld(CellToWorldCenter(cell));
    }

    private HashSet<Vector3Int> ComputePredictedBlastCells(Vector3Int bombCell, int radius)
    {
        HashSet<Vector3Int> cells = new HashSet<Vector3Int>();
        cells.Add(bombCell);

        AddRay(cells, bombCell, Vector3Int.up, radius);
        AddRay(cells, bombCell, Vector3Int.down, radius);
        AddRay(cells, bombCell, Vector3Int.left, radius);
        AddRay(cells, bombCell, Vector3Int.right, radius);

        return cells;
    }

    private void AddRay(HashSet<Vector3Int> cells, Vector3Int origin, Vector3Int dir, int length)
    {
        Vector3Int cur = origin;
        for (int i = 0; i < length; i++)
        {
            cur += dir;

            if (!gridBounds.Contains(new Vector2Int(cur.x, cur.y))) return;

            // si hay pared/indestructible/bomba bloquea
            if (Physics2D.OverlapBox(CellToWorldCenter(cur), Vector2.one * 0.45f, 0f, obstacleMask))
                return;

            cells.Add(cur);

            // si hay destructible, lo incluye pero corta (como bomberman)
            if (destructibleTiles && destructibleTiles.HasTile(cur))
                return;
        }
    }

    // ===================== GRID HELPERS =====================

    private Vector3Int WorldToCell(Vector3 world)
    {
        // usa el grid si está, sino redondeo simple
        if (grid != null) return grid.WorldToCell(world);
        return grid.WorldToCell(world);
    }

    private Vector2 CellToWorldCenter(Vector3Int cell)
    {
        // Centro real de la celda según el Grid (no inventamos offsets)
        Vector3 w = grid.GetCellCenterWorld(cell);
        return new Vector2(w.x, w.y);
    }


    private bool IsWalkableCell(Vector3Int cell)
    {
        if (walkableTiles != null && !walkableTiles.HasTile(cell)) return false;


        // destructible tile bloquea (aunque no tenga collider)
        if (destructibleTiles && destructibleTiles.HasTile(cell)) return false;

        // paredes/bombas/indestructibles por física
        Vector2 world = CellToWorldCenter(cell);
        Collider2D hit = Physics2D.OverlapBox(world, Vector2.one * 0.45f, 0f, obstacleMask);
        return hit == null;
    }

    private IEnumerable<Vector3Int> Neighbors4(Vector3Int c)
    {
        yield return c + Vector3Int.up;
        yield return c + Vector3Int.down;
        yield return c + Vector3Int.left;
        yield return c + Vector3Int.right;
    }

    // ===================== ANIM =====================

    private void AnimateFromVelocity(Vector2 vel)
    {
        // idle
        if (vel.magnitude < 0.05f)
        {
            SetDirection(Vector2.zero, activeSpriteRenderer);
            return;
        }

        // elige sprite por eje dominante (como tu player stick)
        if (Mathf.Abs(vel.x) > Mathf.Abs(vel.y))
            SetDirection(new Vector2(Mathf.Sign(vel.x), 0f), vel.x > 0 ? spriteRendererRight : spriteRendererLeft);
        else
            SetDirection(new Vector2(0f, Mathf.Sign(vel.y)), vel.y > 0 ? spriteRendererUp : spriteRendererDown);
    }

    private void SetDirection(Vector2 dir, AnimatedSpriteRenderer spr)
    {
        spriteRendererUp.enabled = spr == spriteRendererUp;
        spriteRendererDown.enabled = spr == spriteRendererDown;
        spriteRendererLeft.enabled = spr == spriteRendererLeft;
        spriteRendererRight.enabled = spr == spriteRendererRight;

        activeSpriteRenderer = spr;
        activeSpriteRenderer.idle = (dir == Vector2.zero);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            enabled = false;
            if (bomb) bomb.enabled = false;
            rigidbody.velocity = Vector2.zero;
            gameObject.SetActive(false); // o anim de muerte si querés
        }
    }

    private void DebugWhyCellIsNotWalkable(Vector3Int cell)
    {
        Vector2 w = CellToWorldCenter(cell);

        // si falla por WALKPATH
        if (walkableTiles != null && !walkableTiles.HasTile(cell))
        {
            Debug.Log($"[AI] NOT WALKABLE (no WALKPATH tile) cell={cell}");
            return;
        }

        // si falla por destructible
        if (destructibleTiles != null && destructibleTiles.HasTile(cell))
        {
            Debug.Log($"[AI] NOT WALKABLE (destructible tile) cell={cell}");
            return;
        }

        // si falla por física
        Collider2D[] hits = Physics2D.OverlapBoxAll(w, Vector2.one * 0.45f, 0f, obstacleMask);
        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                if (!hits[i]) continue;
                Debug.Log($"[AI] NOT WALKABLE (physics) cell={cell} hit={hits[i].name} layer={LayerMask.LayerToName(hits[i].gameObject.layer)}");
            }
            return;
        }

        Debug.Log($"[AI] WALKABLE cell={cell}");
    }


}
