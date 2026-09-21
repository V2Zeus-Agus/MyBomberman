using UnityEngine;

public class MoveController : MonoBehaviour
{
    public new Rigidbody2D rigidbody { get; private set; }
    private Vector2 direction = Vector2.down;
    public float speed = 5f;

    [Header("Teclado (P1/P2)")]
    public KeyCode inputUp = KeyCode.W;
    public KeyCode inputDown = KeyCode.S;
    public KeyCode inputLeft = KeyCode.A;
    public KeyCode inputRight = KeyCode.D;

    [Header("Player 3 (Gamepad 1)")]
    public bool isPlayer3 = false;
    public string p3HorizontalAxis = "P3_Horizontal";
    public string p3VerticalAxis = "P3_Vertical";
    [Range(0f, 0.6f)] public float p3DeadZone = 0.25f;

    [Header("Player 4 (Gamepad 2)")]
    public bool isPlayer4 = false;
    public string p4HorizontalAxis = "P4_Horizontal";
    public string p4VerticalAxis = "P4_Vertical";
    [Range(0f, 0.6f)] public float p4DeadZone = 0.25f;

    [Header("Sprites")]
    public AnimatedSpriteRenderer spriteRendererUp;
    public AnimatedSpriteRenderer spriteRendererDown;
    public AnimatedSpriteRenderer spriteRendererLeft;
    public AnimatedSpriteRenderer spriteRendererRight;
    public AnimatedSpriteRenderer spriteRenderDeath;
    private AnimatedSpriteRenderer activeSpriteRenderer;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        activeSpriteRenderer = spriteRendererDown;
    }

    void Update()
    {
        if (isPlayer3) ReadStick(p3HorizontalAxis, p3VerticalAxis, p3DeadZone);
        else if (isPlayer4) ReadStick(p4HorizontalAxis, p4VerticalAxis, p4DeadZone);
        else ReadKeyboard(); // P1/P2
    }

    private void FixedUpdate()
    {
        Vector2 position = rigidbody.position;
        Vector2 translation = direction * speed * Time.fixedDeltaTime;
        rigidbody.MovePosition(position + translation);
    }

    private void ReadKeyboard()
    {
        if (Input.GetKey(inputUp)) SetDirection(Vector2.up, spriteRendererUp);
        else if (Input.GetKey(inputDown)) SetDirection(Vector2.down, spriteRendererDown);
        else if (Input.GetKey(inputLeft)) SetDirection(Vector2.left, spriteRendererLeft);
        else if (Input.GetKey(inputRight)) SetDirection(Vector2.right, spriteRendererRight);
        else SetDirection(Vector2.zero, activeSpriteRenderer);
    }


    private void ReadStick(string hAxis, string vAxis, float deadZone)
    {
        float x = Input.GetAxisRaw(hAxis);
        float y = Input.GetAxisRaw(vAxis);
        Vector2 raw = new Vector2(x, y);

        if (raw.magnitude < deadZone)
        {
            SetDirection(Vector2.zero, activeSpriteRenderer);
            return;
        }

        if (Mathf.Abs(x) > Mathf.Abs(y))
            SetDirection(new Vector2(Mathf.Sign(x), 0f), x > 0 ? spriteRendererRight : spriteRendererLeft);
        else
            SetDirection(new Vector2(0f, Mathf.Sign(y)), y > 0 ? spriteRendererUp : spriteRendererDown);
    }

    private void SetDirection(Vector2 newDirection, AnimatedSpriteRenderer spriterenderer)
    {
        direction = newDirection;

        spriteRendererUp.enabled = spriterenderer == spriteRendererUp;
        spriteRendererDown.enabled = spriterenderer == spriteRendererDown;
        spriteRendererLeft.enabled = spriterenderer == spriteRendererLeft;
        spriteRendererRight.enabled = spriterenderer == spriteRendererRight;

        activeSpriteRenderer = spriterenderer;
        activeSpriteRenderer.idle = direction == Vector2.zero;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
            DeathSequence();
    }

    private void DeathSequence()
    {
        enabled = false;
        GetComponent<BombController>().enabled = false;

        // ▶ SFX muerte
        if (AudioManager.Instance != null) AudioManager.Instance.PlayDeath();

        spriteRendererUp.enabled = false;
        spriteRendererDown.enabled = false;
        spriteRendererLeft.enabled = false;
        spriteRendererRight.enabled = false;
        spriteRenderDeath.enabled = true;

        Invoke(nameof(OnDeathSequenceEnded), 1.125f);
    }

    private void OnDeathSequenceEnded()
    {
        gameObject.SetActive(false);
        FindObjectOfType<GameManager>().CheckWinState();
    }
}
