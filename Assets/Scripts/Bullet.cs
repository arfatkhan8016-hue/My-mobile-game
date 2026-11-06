using UnityEngine;

/// <summary>
/// Implement this on anything that can take damage (enemies, crates, vehicles...).
/// The component can sit on the root; bullets find it via GetComponentInParent.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal);
}

/// <summary>
/// Pooled projectile. No Rigidbody, no Collider, no Update():
/// WeaponSystem calls Tick() for all active bullets in one loop.
/// Each tick does a swept Raycast (previous position -> next position), so fast bullets never tunnel.
/// Prefab = empty GameObject + this script + (optional) TrailRenderer for the tracer.
/// </summary>
public class Bullet : MonoBehaviour
{
    [SerializeField] private TrailRenderer trail;      // optional tracer
    [SerializeField] private float impactForce = 0.2f; // physics push on rigidbodies, scaled by damage

    private WeaponSystem owner;
    private Vector3 direction;
    private float speed;
    private float damage;
    private float remainingRange;
    private LayerMask hitMask;

    public void Init(WeaponSystem weapon) => owner = weapon;

    public void Fire(Vector3 position, Vector3 dir, float bulletSpeed, float bulletDamage, float range, LayerMask mask)
    {
        direction = dir;
        speed = bulletSpeed;
        damage = bulletDamage;
        remainingRange = range;
        hitMask = mask;

        transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));
        gameObject.SetActive(true);
        if (trail) trail.Clear();   // no streak from the previous use of this pooled bullet
    }

    /// <summary>Returns false when the bullet is finished (hit something or out of range).</summary>
    public bool Tick(float dt)
    {
        float step = speed * dt;
        Vector3 pos = transform.position;

        if (Physics.Raycast(pos, direction, out RaycastHit hit, step, hitMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeDamage(damage, hit.point, hit.normal);

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(direction * (damage * impactForce), hit.point, ForceMode.Impulse);

            owner.OnBulletHit(hit, target != null);
            return false;
        }

        transform.position = pos + direction * step;
        remainingRange -= step;
        return remainingRange > 0f;
    }
}
