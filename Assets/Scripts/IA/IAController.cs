using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class IAController : MonoBehaviour
{
    private enum AIState { SeekBombSite, Escape }

    [Header("Grilla")]
    public float tileSize = 1f;
    public RectInt gridBounds = new RectInt(-8, -5, 17, 11);
    [Tooltip("Paredes/indestructibles/bombas. NO pongas la capa de la IA.")]
    public LayerMask obstacleMask;
    public Tilemap destructibleTiles;

    [Header("Grid/Bounds automáticos")]
    [SerializeField] private GridLayout grid;
    [SerializeField] private Tilemap boundsTilemap;

    [Header("Movimiento")]
    public float speed = 4f;

    [Header("Movimiento por bloques")]
    [Tooltip("Distancia al centro del tile para considerar que llegó.")]
    public float arriveEps = 0.06f;

    [Header("Bombas")]
    public BombController bomb;
    public float replanEvery = 0.25f;

    [Header("Sprites como Player")]
    public AnimatedSpriteRenderer spriteRendererUp;
    public AnimatedSpriteRenderer spriteRendererDown;
    public AnimatedSpriteRenderer spriteRendererLeft;
    public AnimatedSpriteRenderer spriteRendererRight;
    public AnimatedSpriteRenderer spriteRenderDeath;

    [Header("Cooldown")]
    public float spawnPlantGrace = 1.0f;
    public float postEscapePlantDelay = 1.5f;
    private float nextPlantAllowedTime = 0f;
    private float lastBlastEndTime = 0f;

    // ---- internals ----
    public new Rigidbody2D rigidbody { get; private set; }
    private Vector2Int curTile;
    private Queue<Vector2Int> path = new Queue<Vector2Int>();
    private Vector2 moveDir = Vector2.zero;
    private AnimatedSpriteRenderer activeSpriteRenderer;
    private AIState state = AIState.SeekBombSite;
    private float replanTimer = 0f;

    // Escape control
    private Vector2Int lastBombTile;
    private int lastBombRadius = 1;

    // ---- Anti-atascos ----
    [Header("Anti-atascos")]
    public float stuckTimeout = 0.6f;
    public float stuckRepathCooldown = 0.2f;
    public int recentTrailSize = 6;
    private float stuckTimer = 0f;
    private float lastRepathTime = -999f;
    private Vector2Int? watchingTile = null;
    private float lastProgressDist = float.PositiveInfinity;
    private Vector2Int lastTile;
    private readonly Queue<Vector2Int> recentTrail = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> recentSet = new HashSet<Vector2Int>();

    // ---- Escape seguro ----
    [Header("Escape seguro")]
    public int safeIdleExtra = 2;   // tiles extra por encima del radio de la bomba
    private bool holdPosition = false;

    [Header("Repath dinámico")]
    public float blockedRepathDelay = 0.15f;
    private float lastBlockedRepath = -999f;

    [Header("Anti-loop dirección")]
    public float sameDirMaxTime = 8f;
    private Vector2 lastMoveDir = Vector2.zero;
    private float sameDirTimer = 0f;

    // ---- No acercarse a la bomba mientras está activa ----
    [Header("No acercarse a la bomba")]
    [Tooltip("Tiempo extra tras apagarse la llama durante el que evita pasos que acerquen a la bomba.")]
    public float approachLockBuffer = 0.4f;
    private float doNotApproachUntil = 0f;

    // ---- Walkable ----
    [Header("Walkable (opcional)")]
    [Tooltip("Tilemap con el camino válido. Si se asigna y 'requireWalkable' está activado, la IA solo caminará por celdas que tengan tile aquí.")]
    public Tilemap walkableTiles;
    public bool requireWalkable = true;

    [Header("Colisiones (cast)")]
    [Tooltip("Achica el box de chequeo para no 'raspar' paredes.")]
    public float skinWidth = 0.02f;

    // ---- Watchdog BRUTO (corta bucles de caminar contra pared) ----
    [Header("Watchdog bruto")]
    public float hardMoveTimeout = 8f;        // segundos sin progreso
    public float hardBreakIdleTime = 0.25f;   // pausa tras cortar (opcional)
    private float hardMoveTimer = 0f;
    private Vector2Int watchdogLastTile;
    private float watchdogSleepUntil = 0f;    // ventana de pausa tras cortar

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        if (!bomb) bomb = GetComponent<BombController>();
        rigidbody.gravityScale = 0f;
        rigidbody.freezeRotation = true;

        activeSpriteRenderer = spriteRendererDown;

        // Calcular tile actual ANTES de usarlo en watchdog
        curTile = WorldToTile(rigidbody.position);
        lastTile = curTile;
        watchdogLastTile = curTile;

        RememberTile(curTile);
    }

    void Start()
    {
        if (boundsTilemap != null)
        {
            var b = boundsTilemap.cellBounds;
            gridBounds = new RectInt(b.xMin, b.yMin, b.size.x, b.size.y);
        }
        else if (walkableTiles != null)
        {
            // Si no hay boundsTilemap, usamos los bounds del Walkable
            var b = walkableTiles.cellBounds;
            gridBounds = new RectInt(b.xMin, b.yMin, b.size.x, b.size.y);
        }

        if (grid == null && walkableTiles != null)
            grid = walkableTiles.layoutGrid; // usa el grid del Walkable

        if (grid != null)
            tileSize = grid.cellSize.x;

        nextPlantAllowedTime = Time.time + Mathf.Max(spawnPlantGrace, 0f);
    }

    void Update()
    {
        // WATCHDOG antiguo (lo dejo por compat, pero el nuevo corte está en CheckSameDirectionLoop)
        if (sameDirTimer >= sameDirMaxTime)
        {
            HardCutLoop();
            return;
        }

        if (Time.time < watchdogSleepUntil)
        {
            moveDir = Vector2.zero;
            SetSpriteIdle();
            return;
        }

        // Snap suave al centro del tile actual (NO avanzamos al siguiente desde Update)
        Vector2 center = TileToWorldCenter(curTile);
        if (Vector2.Distance(rigidbody.position, center) <= arriveEps)
            rigidbody.position = center;

        replanTimer -= Time.deltaTime;
        if (replanTimer <= 0f) { Replan(); replanTimer = replanEvery; }

        if (holdPosition)
        {
            moveDir = Vector2.zero;
            SetSpriteIdle();
            CheckSameDirectionLoop();
            return;
        }

        if (path.Count > 0)
        {
            Vector2Int next = path.Peek();

            // 0) Si el próximo tile tiene un DESTRUCTIBLE → tratarlo como bloqueado
            if (IsDestructibleCell(next))
            {
                HandleBlockedNext();
                CheckSameDirectionLoop();
                return;
            }

            // 1) Si el próximo tile está bloqueado por colisión/lógica → cambiar ruta YA
            if (!IsWalkable(next))
            {
                HandleBlockedNext();
                CheckSameDirectionLoop();
                return;
            }

            // 2) Durante el escape y con lock activo: no aceptar pasos que ACERCAN a la bomba
            if (state == AIState.Escape && Time.time < doNotApproachUntil && IsApproachingBomb(curTile, next))
            {
                if (!TryPerpendicularStep())
                {
                    var safe2 = FindNearestSafeTileFrom(curTile, lastBombTile, lastBombRadius);
                    BuildPath(curTile, safe2);
                }
                CheckSameDirectionLoop();
                WatchdogBrutoTick();
                return;
            }

            // Solo para detección de atasco (el avance real se hace en FixedUpdate)
            float dist = Vector2.Distance(rigidbody.position, TileToWorldCenter(next));
            if (watchingTile != next)
            {
                watchingTile = next;
                lastProgressDist = dist;
                stuckTimer = 0f;
            }
            else
            {
                if (dist >= lastProgressDist - 0.001f) stuckTimer += Time.deltaTime;
                else stuckTimer = 0f;
                lastProgressDist = dist;

                if (stuckTimer >= stuckTimeout && Time.time - lastRepathTime >= stuckRepathCooldown)
                {
                    bool moved = false;

                    if (state == AIState.Escape)
                        moved = TryQuickDetour(avoidBomb: true) || TryEscapeRepathFar();
                    else
                    {
                        moved = TryQuickDetour(avoidBomb: false);
                        if (!moved) { lastRepathTime = Time.time; Replan(); }
                    }

                    if (!moved) { lastRepathTime = Time.time; Replan(); }

                    stuckTimer = 0f;
                    lastProgressDist = float.PositiveInfinity;
                }
            }
        }
        else
        {
            moveDir = Vector2.zero;
            SetSpriteIdle();
        }

        CheckSameDirectionLoop();
    }

    // ======== MOVIMIENTO 100% POR BLOQUES (centro a centro) ========
    void FixedUpdate()
    {
        if (path.Count > 0)
        {
            Vector2Int next = path.Peek();
            Vector2 target = TileToWorldCenter(next);
            Vector2 pos = rigidbody.position;

            Vector2 to = target - pos;
            float maxStep = speed * Time.fixedDeltaTime;

            if (to.magnitude <= maxStep)
            {
                // Llego EXACTO al centro del siguiente tile
                rigidbody.MovePosition(target);

                lastTile = curTile;
                curTile = next;
                RememberTile(curTile);
                path.Dequeue();
                UpdateMoveDirToNext(); // refresca sprite/dirección
            }
            else
            {
                rigidbody.MovePosition(pos + to.normalized * maxStep);
            }
            return;
        }

        // Sin path: quieto (no hay arrastre sub-tile)
        moveDir = Vector2.zero;
        SetSpriteIdle();
    }

    // Fuerza un cambio de rumbo agresivo para cortar el bucle
    void HardCutLoop()
    {
        sameDirTimer = 0f;
        Vector2 baseDir = (lastMoveDir != Vector2.zero) ? lastMoveDir : moveDir;
        Vector2Int dir = DirSign(baseDir);

        if (TryPerpendicularFromDir(dir)) return;

        bool moved = (state == AIState.Escape)
            ? TryQuickDetour(avoidBomb: true)
            : TryQuickDetour(avoidBomb: false);
        if (moved) return;

        lastRepathTime = Time.time;
        Replan();
    }

    // Intenta mover 1 tile en alguna perpendicular a la dirección actual
    bool TryPerpendicularFromDir(Vector2Int dirSign)
    {
        if (dirSign == Vector2Int.zero) return false;

        List<Vector2Int> cand = new List<Vector2Int>(2);
        if (dirSign.x != 0)  // venía horizontal → probar arriba/abajo
        {
            cand.Add(new Vector2Int(curTile.x, curTile.y + 1));
            cand.Add(new Vector2Int(curTile.x, curTile.y - 1));
        }
        else                  // venía vertical → probar izquierda/derecha
        {
            cand.Add(new Vector2Int(curTile.x + 1, curTile.y));
            cand.Add(new Vector2Int(curTile.x - 1, curTile.y));
        }

        for (int i = 0; i < cand.Count; i++)
        {
            int j = Random.Range(i, cand.Count);
            (cand[i], cand[j]) = (cand[j], cand[i]);
        }

        foreach (var n in cand)
        {
            if (!InBounds(n)) continue;
            if (!IsWalkable(n)) continue;
            if (n == lastTile || recentSet.Contains(n)) continue;
            if (state == AIState.Escape && InBlastLOS(n, lastBombTile, lastBombRadius)) continue;

            path.Clear();
            path.Enqueue(n);
            UpdateMoveDirToNext();
            return true;
        }
        return false;
    }

    // ---------- PLAN ----------
    void Replan()
    {
        curTile = WorldToTile(rigidbody.position);
        int radiusPlan = bomb != null ? Mathf.Max(1, bomb.explosionRadius) : 1;

        if (state == AIState.SeekBombSite)
        {
            holdPosition = false;

            if (Time.time >= nextPlantAllowedTime && HasDestructibleNeighbor(curTile))
            {
                var escapePath = GetEscapePath(curTile, radiusPlan);
                if (escapePath != null && escapePath.Count > 0 && bomb != null && bomb.TryPlaceBomb())
                {
                    lastBombTile = curTile;
                    lastBombRadius = radiusPlan;
                    lastBlastEndTime = Time.time + bomb.bombFuseTime + bomb.explosionDuration;

                    doNotApproachUntil = lastBlastEndTime + approachLockBuffer;

                    path = escapePath;
                    UpdateMoveDirToNext();
                    state = AIState.Escape;
                    return;
                }
            }

            var site = FindNearestBombSiteWithEscape(curTile, radiusPlan);
            BuildPath(curTile, site);
            if (site == null) TryStepAnywhere();
        }
        else // Escape
        {
            if (Time.time < lastBlastEndTime)
            {
                int dist = Manhattan(curTile, lastBombTile);
                bool farEnough = dist >= (lastBombRadius + safeIdleExtra);
                bool outOfCross = !InBlastLOS(curTile, lastBombTile, lastBombRadius);

                if (farEnough && outOfCross)
                {
                    holdPosition = true;
                    path.Clear();
                    moveDir = Vector2.zero;
                    SetSpriteIdle();
                    return;
                }

                if (farEnough && !outOfCross)
                {
                    if (TryPerpendicularStep())
                        return;
                }
            }

            bool inActiveBlast = IsInActiveBlast(curTile);
            if (inActiveBlast || path.Count == 0)
            {
                holdPosition = false;
                var safe = FindNearestSafeTileFrom(curTile, lastBombTile, lastBombRadius);
                BuildPath(curTile, safe);
            }

            if (Time.time >= lastBlastEndTime && !InBlastLOS(curTile, lastBombTile, lastBombRadius))
            {
                nextPlantAllowedTime = Time.time + postEscapePlantDelay;
                holdPosition = false;
                path.Clear();
                moveDir = Vector2.zero;
                SetSpriteIdle();
                state = AIState.SeekBombSite;
            }
        }
    }

    bool IsInActiveBlast(Vector2Int t)
    {
        return Time.time < lastBlastEndTime && InBlastLOS(t, lastBombTile, lastBombRadius);
    }

    // ---- CHEQUEOS CLAVE ----
    bool HasDestructibleNeighbor(Vector2Int t)
    {
        if (!destructibleTiles) return false;
        foreach (var n in Neigh4(t))
        {
            var c = new Vector3Int(n.x, n.y, 0);
            if (destructibleTiles.HasTile(c)) return true;
        }
        return false;
    }

    Vector2Int? FindNearestBombSiteWithEscape(Vector2Int start, int r)
    {
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        HashSet<Vector2Int> vis = new HashSet<Vector2Int>();
        q.Enqueue(start); vis.Add(start);

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (IsWalkable(t) && HasDestructibleNeighbor(t))
            {
                var escape = GetEscapePath(t, r);
                if (escape != null && escape.Count > 0) return t;
            }

            foreach (var n in Neigh4(t))
                if (InBounds(n) && !vis.Contains(n) && IsWalkable(n))
                { vis.Add(n); q.Enqueue(n); }
        }
        return null;
    }

    Queue<Vector2Int> GetEscapePath(Vector2Int from, int r)
    {
        Vector2Int? safe = FindNearestSafeTileFrom(from, from, r);
        if (!safe.HasValue) return null;
        return BuildPathInternal(from, safe.Value);
    }

    Vector2Int? FindNearestSafeTileFrom(Vector2Int start, Vector2Int bombTile, int r)
    {
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        HashSet<Vector2Int> vis = new HashSet<Vector2Int>();
        q.Enqueue(start); vis.Add(start);

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (IsWalkable(t) && !InBlastLOS(t, bombTile, r))
                return t;

            foreach (var n in Neigh4(t))
                if (InBounds(n) && !vis.Contains(n) && IsWalkable(n))
                { vis.Add(n); q.Enqueue(n); }
        }
        return null;
    }

    bool InBlastLOS(Vector2Int t, Vector2Int bombTile, int r)
    {
        if (t == bombTile) return true;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Vector2Int c = bombTile;
            for (int i = 1; i <= r; i++)
            {
                c += d;
                if (!InBounds(c)) break;
                if (IsObstacleCell(c)) break;
                if (c == t) return true;
            }
        }
        return false;
    }

    bool IsObstacleCell(Vector2Int cell)
    {
        Vector2 c = TileToWorldCenter(cell);
        Vector2 box = Vector2.one * (tileSize * 0.8f);
        var cols = Physics2D.OverlapBoxAll(c, box, 0f, obstacleMask);
        foreach (var col in cols)
            if (!col.isTrigger) return true;
        return false;
    }

    void BuildPath(Vector2Int start, Vector2Int? goalOpt)
    {
        path.Clear();
        if (!goalOpt.HasValue || goalOpt.Value == start) { moveDir = Vector2.zero; SetSpriteIdle(); return; }
        path = BuildPathInternal(start, goalOpt.Value);
        UpdateMoveDirToNext();

        watchingTile = null;
        lastProgressDist = float.PositiveInfinity;
        stuckTimer = 0f;
    }

    Queue<Vector2Int> BuildPathInternal(Vector2Int start, Vector2Int goal)
    {
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();
        HashSet<Vector2Int> vis = new HashSet<Vector2Int>();
        q.Enqueue(start); vis.Add(start);

        bool found = false;
        while (q.Count > 0 && !found)
        {
            var t = q.Dequeue();
            foreach (var n in Neigh4(t))
            {
                if (!InBounds(n) || !IsWalkable(n) || vis.Contains(n)) continue;

                // Durante escape con lock: no expandir vecinos que ACERCAN a la bomba
                if (state == AIState.Escape && Time.time < doNotApproachUntil && IsApproachingBomb(t, n))
                    continue;

                parent[n] = t; vis.Add(n); q.Enqueue(n);
                if (n == goal) { found = true; break; }
            }
        }

        if (!found) return new Queue<Vector2Int>();

        Stack<Vector2Int> rev = new Stack<Vector2Int>();
        var cur = goal;
        while (cur != start) { rev.Push(cur); cur = parent[cur]; }
        Queue<Vector2Int> result = new Queue<Vector2Int>();
        while (rev.Count > 0) result.Enqueue(rev.Pop());
        return result;
    }

    void UpdateMoveDirToNext()
    {
        if (path.Count == 0) { moveDir = Vector2.zero; SetSpriteIdle(); return; }

        Vector2Int next = path.Peek();
        Vector2Int d = new Vector2Int(next.x - curTile.x, next.y - curTile.y);

        if (d.x > 0) SetDir(Vector2.right, spriteRendererRight);
        else if (d.x < 0) SetDir(Vector2.left, spriteRendererLeft);
        else if (d.y > 0) SetDir(Vector2.up, spriteRendererUp);
        else if (d.y < 0) SetDir(Vector2.down, spriteRendererDown);
        else SetSpriteIdle();
    }

    Vector2Int WorldToTile(Vector2 p)
    {
        if (grid != null)
        {
            Vector3Int c = grid.WorldToCell(p);
            return new Vector2Int(c.x, c.y);
        }
        int x = Mathf.RoundToInt(p.x / tileSize);
        int y = Mathf.RoundToInt(p.y / tileSize);
        return new Vector2Int(x, y);
    }

    Vector2 TileToWorldCenter(Vector2Int t)
    {
        if (grid != null)
        {
            Vector3 w = grid.CellToWorld(new Vector3Int(t.x, t.y, 0));
            return new Vector2(w.x + grid.cellSize.x * 0.5f, w.y + grid.cellSize.y * 0.5f);
        }
        return new Vector2(t.x * tileSize, t.y * tileSize);
    }

    bool InBounds(Vector2Int t)
    {
        return t.x >= gridBounds.xMin && t.x < gridBounds.xMax &&
               t.y >= gridBounds.yMin && t.y < gridBounds.yMax;
    }

    IEnumerable<Vector2Int> Neigh4(Vector2Int t)
    {
        yield return new Vector2Int(t.x + 1, t.y);
        yield return new Vector2Int(t.x - 1, t.y);
        yield return new Vector2Int(t.x, t.y + 1);
        yield return new Vector2Int(t.x, t.y - 1);
    }

    // ======= NUEVO IsWalkable (Walkable + destructibles + skinWidth) =======
    bool IsWalkable(Vector2Int t)
    {
        if (!InBounds(t)) return false;

        // Si usás Tilemap Walkable y lo exigís
        if (requireWalkable && !IsWalkableByTilemap(t))
            return false;

        // Jamás pisar un destructible
        if (IsDestructibleCell(t))
            return false;

        // Cast físico con "skin" para no rozar paredes
        Vector2 c = TileToWorldCenter(t);
        float size = Mathf.Max(0.01f, tileSize - 2f * skinWidth);
        Vector2 box = new Vector2(size, size);
        var cols = Physics2D.OverlapBoxAll(c, box, 0f, obstacleMask);
        foreach (var col in cols)
        {
            if (!col.isTrigger) return false; // bloquea solo sólidos (pared/bomba/etc)
        }
        return true;
    }

    bool IsWalkableByTilemap(Vector2Int t)
    {
        if (!walkableTiles) return true; // si no se asigna, no restringe
        return walkableTiles.HasTile(new Vector3Int(t.x, t.y, 0));
    }

    bool IsDestructibleCell(Vector2Int t)
    {
        if (!destructibleTiles) return false;
        return destructibleTiles.HasTile(new Vector3Int(t.x, t.y, 0));
    }

    void TryStepAnywhere()
    {
        foreach (var n in Neigh4(curTile))
        {
            if (!IsWalkable(n)) continue;
            if (state == AIState.Escape && Time.time < doNotApproachUntil && IsApproachingBomb(curTile, n)) continue;

            path.Enqueue(n);
            UpdateMoveDirToNext();
            return;
        }
        moveDir = Vector2.zero;
    }

    bool TryQuickDetour(bool avoidBomb)
    {
        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        Vector2Int best = curTile;
        float bestScore = float.NegativeInfinity;

        foreach (var d in dirs)
        {
            var n = new Vector2Int(curTile.x + d.x, curTile.y + d.y);
            if (!IsWalkable(n)) continue;
            if (n == lastTile) continue;
            if (recentSet.Contains(n)) continue;

            if (avoidBomb)
            {
                if (InBlastLOS(n, lastBombTile, lastBombRadius)) continue;
                if (Time.time < doNotApproachUntil && IsApproachingBomb(curTile, n)) continue;
            }

            float score = 0f;
            if (avoidBomb) score += Manhattan(n, lastBombTile) * 2f;
            score += Random.Range(-0.1f, 0.1f);

            if (score > bestScore) { bestScore = score; best = n; }
        }

        if (best != curTile)
        {
            path.Clear();
            path.Enqueue(best);
            UpdateMoveDirToNext();
            return true;
        }
        return false;
    }

    bool TryEscapeRepathFar()
    {
        var target = FindFarthestSafeTileFrom(curTile, lastBombTile, lastBombRadius);
        if (target.HasValue)
        {
            BuildPath(curTile, target);
            return path.Count > 0;
        }
        return false;
    }

    Vector2Int? FindFarthestSafeTileFrom(Vector2Int start, Vector2Int bombTile, int r, int maxExplore = 300)
    {
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        HashSet<Vector2Int> vis = new HashSet<Vector2Int>();
        q.Enqueue(start); vis.Add(start);

        Vector2Int? best = null;
        int bestDist = -1;
        int explored = 0;

        while (q.Count > 0 && explored < maxExplore)
        {
            var t = q.Dequeue(); explored++;
            if (IsWalkable(t) && !InBlastLOS(t, bombTile, r) && !recentSet.Contains(t))
            {
                if (state == AIState.Escape && Time.time < doNotApproachUntil && IsApproachingBomb(start, t))
                {
                    // skip
                }
                else
                {
                    int d = Manhattan(t, bombTile);
                    if (d > bestDist) { bestDist = d; best = t; }
                }
            }

            foreach (var n in Neigh4(t))
            {
                if (InBounds(n) && !vis.Contains(n) && IsWalkable(n))
                {
                    if (state == AIState.Escape && Time.time < doNotApproachUntil && IsApproachingBomb(t, n)) { }
                    else { vis.Add(n); q.Enqueue(n); }
                }
            }
        }
        return best;
    }

    int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    void RememberTile(Vector2Int t)
    {
        if (recentSet.Add(t))
        {
            recentTrail.Enqueue(t);
            if (recentTrail.Count > recentTrailSize)
                recentSet.Remove(recentTrail.Dequeue());
        }
    }

    void SetDir(Vector2 dir, AnimatedSpriteRenderer sr)
    {
        moveDir = dir;
        spriteRendererUp.enabled = sr == spriteRendererUp;
        spriteRendererDown.enabled = sr == spriteRendererDown;
        spriteRendererLeft.enabled = sr == spriteRendererLeft;
        spriteRendererRight.enabled = sr == spriteRendererRight;
        if (sr) { sr.idle = false; activeSpriteRenderer = sr; }
    }
    void SetSpriteIdle() { if (activeSpriteRenderer) activeSpriteRenderer.idle = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
            DeathSequence();
    }
    void DeathSequence()
    {
        enabled = false;
        if (bomb) bomb.enabled = false;

        spriteRendererUp.enabled = spriteRendererDown.enabled =
        spriteRendererLeft.enabled = spriteRendererRight.enabled = false;
        if (spriteRenderDeath) spriteRenderDeath.enabled = true;

        Invoke(nameof(FinishDeath), 1.125f);
    }
    void FinishDeath()
    {
        gameObject.SetActive(false);
        FindObjectOfType<GameManager>()?.CheckWinState();
    }

    void HandleBlockedNext()
    {
        if (Time.time - lastBlockedRepath < blockedRepathDelay) return;
        lastBlockedRepath = Time.time;

        bool moved = false;

        if (state == AIState.Escape)
            moved = TryQuickDetour(avoidBomb: true) || TryEscapeRepathFar();
        else
            moved = TryQuickDetour(avoidBomb: false);

        if (!moved)
        {
            lastRepathTime = Time.time;
            Replan();
        }
    }

    // ==================== ANTI-LOOP DIRECCIÓN (REEMPLAZADA) ====================
    void CheckSameDirectionLoop()
    {
        // dirección real (según el próximo tile del path si lo hay)
        Vector2Int curSign = GetCurrentDirSign();
        if (curSign == Vector2Int.zero)
        {
            sameDirTimer = 0f;
            lastMoveDir = Vector2.zero;
            return;
        }

        // acumular tiempo con misma dirección
        if (curSign == DirSign(lastMoveDir)) sameDirTimer += Time.deltaTime;
        else { sameDirTimer = 0f; lastMoveDir = new Vector2(curSign.x, curSign.y); }

        // medir progreso hacia el próximo tile (si hay)
        bool noProgress = false;
        if (path.Count > 0)
        {
            Vector2Int next = path.Peek();
            float dist = Vector2.Distance(rigidbody.position, TileToWorldCenter(next));
            if (dist >= lastProgressDist - 0.001f) noProgress = true;
            lastProgressDist = dist;
        }

        // Disparar corte: 1) pasó el umbral; 2) sin progreso por la mitad del umbral
        if (sameDirTimer >= sameDirMaxTime || (noProgress && sameDirTimer >= sameDirMaxTime * 0.5f))
        {
            sameDirTimer = 0f;
            lastMoveDir = Vector2.zero;
            CutLoopAndReplan(curSign);
        }
    }

    // ----- HELPERS del watchdog -----
    Vector2Int GetCurrentDirSign()
    {
        if (path.Count > 0)
        {
            Vector2Int next = path.Peek();
            Vector2 to = TileToWorldCenter(next) - (Vector2)rigidbody.position;
            return DirSign(to);
        }
        return DirSign(moveDir);
    }

    void CutLoopAndReplan(Vector2Int curSign)
    {
        if (TryPerpendicularFromSign(curSign)) return;

        bool moved = (state == AIState.Escape)
            ? TryQuickDetour(avoidBomb: true)
            : TryQuickDetour(avoidBomb: false);
        if (moved) return;

        lastRepathTime = Time.time;
        if (state == AIState.Escape)
        {
            var safe = FindNearestSafeTileFrom(curTile, lastBombTile, lastBombRadius);
            BuildPath(curTile, safe);
        }
        else
        {
            int r = bomb != null ? Mathf.Max(1, bomb.explosionRadius) : 1;
            var site = FindNearestBombSiteWithEscape(curTile, r);
            BuildPath(curTile, site);
            if (site == null) TryStepAnywhere();
        }
    }

    bool TryPerpendicularFromSign(Vector2Int dirSign)
    {
        return TryPerpendicularFromDir(dirSign);
    }

    // ---- utilidades específicas ----

    // ¿El paso propuesto acerca a la IA a su propia bomba?
    bool IsApproachingBomb(Vector2Int from, Vector2Int to)
    {
        return Manhattan(to, lastBombTile) < Manhattan(from, lastBombTile);
    }

    // Si estoy lejos pero aún en la línea de fuego, doy un paso perpendicular
    bool TryPerpendicularStep()
    {
        List<Vector2Int> opts = new List<Vector2Int>(2);

        if (curTile.x == lastBombTile.x) // misma columna → moverse izquierda/derecha
        {
            opts.Add(new Vector2Int(curTile.x - 1, curTile.y));
            opts.Add(new Vector2Int(curTile.x + 1, curTile.y));
        }
        else if (curTile.y == lastBombTile.y) // misma fila → moverse arriba/abajo
        {
            opts.Add(new Vector2Int(curTile.x, curTile.y + 1));
            opts.Add(new Vector2Int(curTile.x, curTile.y - 1));
        }
        else return false;

        for (int i = 0; i < opts.Count; i++)
        {
            int j = Random.Range(i, opts.Count);
            var tmp = opts[i]; opts[i] = opts[j]; opts[j] = tmp;
        }

        foreach (var n in opts)
        {
            if (!InBounds(n)) continue;
            if (!IsWalkable(n)) continue;
            if (recentSet.Contains(n) || n == lastTile) continue;

            path.Clear();
            path.Enqueue(n);
            UpdateMoveDirToNext();
            holdPosition = false;
            return true;
        }
        return false;
    }

    // ----- Helpers de dirección: proyectan a {-1,0,1} en cada eje -----
    private Vector2Int DirSign(Vector2 v)
    {
        int sx = (v.x > 0.01f) ? 1 : (v.x < -0.01f) ? -1 : 0;
        int sy = (v.y > 0.01f) ? 1 : (v.y < -0.01f) ? -1 : 0;
        return new Vector2Int(sx, sy);
    }

    private Vector2Int DirSign(Vector2Int v)
    {
        int sx = v.x == 0 ? 0 : (v.x > 0 ? 1 : -1);
        int sy = v.y == 0 ? 0 : (v.y > 0 ? 1 : -1);
        return new Vector2Int(sx, sy);
    }

    // Tiquea el watchdog: si no cambiamos de tile en hardMoveTimeout, cortamos.
    void WatchdogBrutoTick()
    {
        // si cambió de tile, reseteo el temporizador
        if (curTile != watchdogLastTile)
        {
            watchdogLastTile = curTile;
            hardMoveTimer = 0f;
            return;
        }

        // sólo cuenta cuando estamos intentando movernos
        if (path.Count > 0 || moveDir != Vector2.zero)
            hardMoveTimer += Time.deltaTime;
        else
            hardMoveTimer = 0f;

        if (hardMoveTimer < hardMoveTimeout) return;

        // ¡CORTAR!
        hardMoveTimer = 0f;
        ForceHardBreak();
    }

    // Corta agresivamente la situación de “me quedé caminando contra nada”
    void ForceHardBreak()
    {
        // 1) Intentá un paso perpendicular a la dirección actual
        var baseDir = (lastMoveDir != Vector2.zero) ? lastMoveDir : moveDir;
        var sign = DirSign(baseDir);
        if (TryPerpendicularFromDir(sign)) return;

        // 2) Intentá un desvío rápido (seguro si estamos escapando)
        bool moved = (state == AIState.Escape)
            ? (TryQuickDetour(avoidBomb: true) || TryEscapeRepathFar())
            : TryQuickDetour(avoidBomb: false);

        if (moved) return;

        // 3) Replan global inmediato
        lastRepathTime = Time.time;
        Replan();
        if (path.Count > 0) return;

        // 4) Último recurso: quedate quieto un toque (y no te acerques a la bomba)
        holdPosition = state == AIState.Escape && !InBlastLOS(curTile, lastBombTile, lastBombRadius);
        path.Clear();
        moveDir = Vector2.zero;
        SetSpriteIdle();
        watchdogSleepUntil = Time.time + hardBreakIdleTime;
    }
}
