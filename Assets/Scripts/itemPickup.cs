using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class itemPickup : MonoBehaviour
{
    public enum ItemType
    {
        ExtraBomb,
        BlastRadius,
        SpeedIncrease,
    }

    public ItemType type;

    [Header("Config (opcional)")]
    [SerializeField] private int rangeMax = 8;    // tope de radio
    [SerializeField] private float speedAmount = 1f;   // incremento de velocidad
    [SerializeField] private float speedMaxPlayer = 10f;   // tope p/ jugadores
    [SerializeField] private float speedMaxIA = 8f;   // tope p/ IA

    private void OnItemPickup(GameObject player)
    {
        // Componentes que pueden estar en Player o en IA
        BombController bc = player.GetComponent<BombController>();
        MoveController mv = player.GetComponent<MoveController>(); // players
        IAController ia = player.GetComponent<IAController>();   // IA

        switch (type)
        {
            case ItemType.ExtraBomb:
                if (bc != null) bc.AddBomb();
                break;

            case ItemType.BlastRadius:
                if (bc != null)
                    bc.explosionRadius = Mathf.Min(bc.explosionRadius + 1, rangeMax);
                break;

            case ItemType.SpeedIncrease:
                // Player (MoveController)
                if (mv != null)
                    mv.speed = Mathf.Min(mv.speed + speedAmount, speedMaxPlayer);

                // IA (IAController)
                if (ia != null)
                    ia.speed = Mathf.Min(ia.speed + speedAmount, speedMaxIA);
                break;
        }
        // ▶ SFX power-up
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.GetComponent<IAController>() != null)
        {
            OnItemPickup(other.gameObject);
        }
    }
}
