using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Weapon (default values = P90 SMG). Put this on the P90 object (child of the character's hand bone).
/// - Bullets start at the muzzle but are aimed at the screen-center crosshair point (true TPS aiming).
/// - Bullets, impact effects and muzzle flash are pooled / emitted: zero Instantiate during gameplay.
/// - UI hooks: StartFiring / StopFiring / Reload / ToggleAim / SetAim.
/// - Editor keys: F = fire (hold), R = reload, Q = toggle aim.
/// </summary>
public class WeaponSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ThirdPersonCamera cameraRig;
    [SerializeField] private TPSCharacterController character;   // optional: turns body toward camera while shooting
    [SerializeField] private Transform muzzle;                   // empty child at barrel tip, blue (Z) arrow pointing out
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private AudioSource audioSource;            // optional
    [SerializeField] private AudioClip fireClip;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip emptyClip;
    [SerializeField] private TMP_Text ammoText;                  // optional HUD text

    [Header("Pools")]
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private ParticleSystem impactPrefab;        // optional
    [SerializeField] private int bulletPoolSize = 30;
    [SerializeField] private int impactPoolSize = 8;

    [Header("Weapon Stats (P90)")]
    [SerializeField] private float damage = 18f;
    [SerializeField] private float fireRateRpm = 900f;
    [SerializeField] private int magazineSize = 50;
    [SerializeField] private int reserveAmmo = 150;
    [SerializeField] private float reloadTime = 2.2f;
    [SerializeField] private float bulletSpeed = 250f;
    [SerializeField] private float range = 120f;
    [SerializeField] private bool fullAuto = true;

    [Header("Spread (degrees)")]
    [SerializeField] private float baseSpread = 0.5f;
    [SerializeField] private float spreadPerShot = 0.12f;
    [SerializeField] private float maxSpread = 3f;
    [SerializeField] private float spreadRecoverRate = 5f;       // degrees / second
    [SerializeField, Range(0.1f, 1f)] private float adsSpreadMultiplier = 0.5f;

    [Header("Recoil (camera, degrees per shot)")]
    [SerializeField] private float recoilPitch = 0.55f;
    [SerializeField] private float recoilYaw = 0.25f;
    [SerializeField, Range(0.1f, 1f)] private float adsRecoilMultiplier = 0.6f;

    [Header("Aiming")]
    [SerializeField] private LayerMask hitMask = ~0;             // EXCLUDE the Player layer
    [SerializeField] private float maxAimDistance = 150f;

    // ---- Public state / events for HUD ----
    public int Magazine => magazine;
    public int Reserve => reserveAmmo;
    public bool IsReloading => reloading;
    public bool IsAiming => aiming;
    public event Action<int, int> AmmoChanged;                   // (magazine, reserve)
    public event Action<bool> HitConfirmed;                      // true if the target was damageable (hit marker)

    private readonly Stack<Bullet> freeBullets = new Stack<Bullet>(64);
    private readonly List<Bullet> activeBullets = new List<Bullet>(64);
    private ParticleSystem[] impacts;
    private int impactIndex;

    private Camera cam;
    private static readonly Vector3 ScreenCenter = new Vector3(0.5f, 0.5f, 0f);

    private int magazine;
    private float fireInterval;
    private float nextFireTime;
    private float lastShotTime = -10f;
    private float currentSpread;
    private bool reloading;
    private float reloadEndTime;
    private bool aiming;

    private bool uiTrigger, uiPulse, editorTrigger, prevTrigger;

    private void Awake()
    {
        cam = cameraRig.GetComponent<Camera>();
        magazine = magazineSize;
        fireInterval = 60f / Mathf.Max(1f, fireRateRpm);
        currentSpread = baseSpread;

        Transform poolRoot = new GameObject("WeaponPools").transform; // not parented: bullets must not follow the gun

        for (int i = 0; i < bulletPoolSize; i++)
        {
            Bullet b = Instantiate(bulletPrefab, poolRoot);
            b.Init(this);
            b.gameObject.SetActive(false);
            freeBullets.Push(b);
        }

        if (impactPrefab)
        {
            impacts = new ParticleSystem[impactPoolSize];
            for (int i = 0; i < impactPoolSize; i++)
                impacts[i] = Instantiate(impactPrefab, poolRoot);
        }
    }

    private void Start() => NotifyAmmo();

    // ------------------------------------------------------------------ UI hooks
    public void StartFiring() { uiTrigger = true; uiPulse = true; }
    public void StopFiring() => uiTrigger = false;
    public void Reload() => StartReload();
    public void ToggleAim() => SetAim(!aiming);

    public void SetAim(bool value)
    {
        aiming = value;
        cameraRig.SetAiming(value);
    }

    // ------------------------------------------------------------------ Main loop
    private void Update()
    {
        float dt = Time.deltaTime;

#if UNITY_EDITOR || UNITY_STANDALONE
        ReadEditorInput();
#endif
        bool trigger = uiTrigger || uiPulse || editorTrigger;
        bool wantFire = fullAuto ? trigger : (trigger && !prevTrigger);
        prevTrigger = trigger;
        uiPulse = false;

        if (reloading)
        {
            if (Time.time >= reloadEndTime) FinishReload();
        }
        else if (wantFire && Time.time >= nextFireTime)
        {
            if (magazine > 0)
            {
                if (Time.time - nextFireTime > fireInterval) nextFireTime = Time.time; // long idle: resync
                nextFireTime += fireInterval;
                FireOnce();
            }
            else
            {
                nextFireTime = Time.time + 0.3f;               // throttle "empty" click
                if (!StartReload() && audioSource && emptyClip) audioSource.PlayOneShot(emptyClip);
            }
        }

        currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, spreadRecoverRate * dt);

        TickBullets(dt);

        if (character)
            character.faceCameraDirection = aiming || trigger || (Time.time - lastShotTime) < 1.5f;
    }

    // ------------------------------------------------------------------ Shooting
    private void FireOnce()
    {
        if (freeBullets.Count == 0) return;

        magazine--;
        lastShotTime = Time.time;

        Bullet b = freeBullets.Pop();
        b.Fire(muzzle.position, GetShotDirection(), bulletSpeed, damage, range, hitMask);
        activeBullets.Add(b);

        if (muzzleFlash) muzzleFlash.Emit(1);
        if (audioSource && fireClip) audioSource.PlayOneShot(fireClip);

        // Spread bloom + camera recoil
        currentSpread = Mathf.Min(currentSpread + spreadPerShot, maxSpread);
        float k = aiming ? adsRecoilMultiplier : 1f;
        cameraRig.AddRecoil(recoilPitch * k, UnityEngine.Random.Range(-recoilYaw, recoilYaw) * k);

        NotifyAmmo();
    }

    private Vector3 GetShotDirection()
    {
        // 1) What is under the crosshair?
        Ray ray = cam.ViewportPointToRay(ScreenCenter);
        Vector3 aimPoint = Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, hitMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(maxAimDistance);

        // 2) Muzzle -> aim point (fallback to camera forward if the target is practically behind the muzzle)
        Vector3 dir = aimPoint - muzzle.position;
        dir.Normalize();
        if (Vector3.Dot(dir, ray.direction) < 0.3f) dir = ray.direction;

        // 3) Random cone spread
        float spread = currentSpread * (aiming ? adsSpreadMultiplier : 1f);
        if (spread > 0.001f)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
            dir = Quaternion.LookRotation(dir) * new Vector3(r.x, r.y, 1f);
            dir.Normalize();
        }
        return dir;
    }

    private void TickBullets(float dt)
    {
        // Backwards + swap-remove: O(1) removal, no allocations
        for (int i = activeBullets.Count - 1; i >= 0; i--)
        {
            Bullet b = activeBullets[i];
            if (b.Tick(dt)) continue;

            int last = activeBullets.Count - 1;
            activeBullets[i] = activeBullets[last];
            activeBullets.RemoveAt(last);

            b.gameObject.SetActive(false);
            freeBullets.Push(b);
        }
    }

    /// <summary>Called by Bullet when it hits something.</summary>
    public void OnBulletHit(RaycastHit hit, bool hitDamageable)
    {
        if (impacts != null)
        {
            ParticleSystem ps = impacts[impactIndex];
            impactIndex = (impactIndex + 1) % impacts.Length;
            ps.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(hit.normal));
            ps.Play();
        }
        if (hitDamageable) HitConfirmed?.Invoke(true);
    }

    // ------------------------------------------------------------------ Reload & ammo
    private bool StartReload()
    {
        if (reloading || magazine >= magazineSize || reserveAmmo <= 0) return false;

        reloading = true;
        reloadEndTime = Time.time + reloadTime;
        if (audioSource && reloadClip) audioSource.PlayOneShot(reloadClip);
        if (ammoText) ammoText.SetText("RELOADING");
        return true;
    }

    private void FinishReload()
    {
        int take = Mathf.Min(magazineSize - magazine, reserveAmmo);
        magazine += take;
        reserveAmmo -= take;
        reloading = false;
        NotifyAmmo();
    }

    private void NotifyAmmo()
    {
        if (ammoText) ammoText.SetText("{0:0} / {1:0}", magazine, reserveAmmo); // allocation-free
        AmmoChanged?.Invoke(magazine, reserveAmmo);
    }

    private void OnDisable()
    {
        uiTrigger = uiPulse = editorTrigger = false;
        if (reloading) { reloading = false; NotifyAmmo(); }
    }

    // ------------------------------------------------------------------ Editor fallback
#if UNITY_EDITOR || UNITY_STANDALONE
    private void ReadEditorInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        editorTrigger = Input.GetKey(KeyCode.F);
        if (Input.GetKeyDown(KeyCode.R)) StartReload();
        if (Input.GetKeyDown(KeyCode.Q)) ToggleAim();
#elif ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        editorTrigger = kb.fKey.isPressed;
        if (kb.rKey.wasPressedThisFrame) StartReload();
        if (kb.qKey.wasPressedThisFrame) ToggleAim();
#endif
    }
#endif
}
