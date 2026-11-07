using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Builds a complete playable test scene at runtime (no manual scene editing needed):
/// ground, walls, targets, capsule player, TPS camera, HUD (joystick, look pad, buttons), weapon.
/// Put in Assets/Scripts/. Skips itself if a TPSCharacterController already exists in the scene.
/// Player layer = 8 (unnamed user layer, so no TagManager edits are needed).
/// </summary>
public static class GameBootstrap
{
    private const int PlayerLayer = 8;
    private const int EnemyLayer = 9;
    private static Material baseMat;
    private static Transform playerTf;
    private static GameObject[] enemyPrefabs;

    private static bool hooked;
    private static bool begun;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (!hooked)
        {
            hooked = true;
            // "MENU" button reloads the scene -> show the lobby again
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => { begun = false; Begin(); };
        }
        Begin();
    }

    private static void Begin()
    {
        if (begun) return;
        if (Object.FindObjectOfType<TPSCharacterController>() != null) return;
        begun = true;

        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        enemyPrefabs = Resources.LoadAll<GameObject>("Enemies");
        Lobby.Show(StartGame);
    }

    private static void StartGame()
    {
        BuildWorld();
        PlayerHealth health;
        WeaponSystem weapon = BuildPlayerAndWeapon(out health);
        BuildHud(weapon, health);
        SpawnEnemies(playerTf, health);
    }

    internal static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.InputSystem.UI.InputSystemUIInputModule module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        module.AssignDefaultActions();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    internal static Font GetFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    // ------------------------------------------------------------------ World
    private static void BuildWorld()
    {
        foreach (string n in new[] { "Ground", "TestCube", "Main Camera" })
        {
            GameObject g = GameObject.Find(n);
            if (g) Object.Destroy(g);
        }

        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject l = new GameObject("Sun");
            Light light = l.AddComponent<Light>();
            light.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        bool hasCity = TryBuildCity();

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = hasCity ? new Vector3(40f, 1f, 40f) : new Vector3(20f, 1f, 20f);
        ground.GetComponent<Renderer>().sharedMaterial = Mat(hasCity ? new Color(0.24f, 0.25f, 0.27f) : new Color(0.28f, 0.45f, 0.28f));

        if (!hasCity)   // fallback blocks when no city models were uploaded
        {
            MakeBlock(new Vector3(6f, 1.5f, 12f), new Vector3(8f, 3f, 1f), new Color(0.55f, 0.5f, 0.45f));
            MakeBlock(new Vector3(-8f, 1f, 18f), new Vector3(1f, 2f, 10f), new Color(0.5f, 0.5f, 0.55f));
            MakeBlock(new Vector3(0f, 0.5f, 25f), new Vector3(12f, 1f, 1f), new Color(0.6f, 0.45f, 0.4f));
        }

        BuildProps();

        if (enemyPrefabs.Length == 0)   // dummy targets only if no enemy models were uploaded
        {
            MakeTarget(new Vector3(0f, 1f, 14f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.9f, 0.3f, 0.2f));
            MakeTarget(new Vector3(-4f, 1f, 20f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.9f, 0.5f, 0.1f));
            MakeTarget(new Vector3(4f, 0.75f, 22f), PrimitiveType.Cube, new Vector3(1.5f, 1.5f, 1.5f), new Color(0.8f, 0.2f, 0.5f));
            MakeTarget(new Vector3(10f, 1f, 8f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.2f, 0.5f, 0.9f));
            MakeTarget(new Vector3(-10f, 1f, 6f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.6f, 0.2f, 0.8f));
        }
    }

    private static void MakeBlock(Vector3 pos, Vector3 scale, Color c)
    {
        GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = "Block";
        b.transform.position = pos;
        b.transform.localScale = scale;
        b.GetComponent<Renderer>().sharedMaterial = Mat(c);
    }

    private static void MakeTarget(Vector3 pos, PrimitiveType type, Vector3 scale, Color c)
    {
        GameObject t = GameObject.CreatePrimitive(type);
        t.name = "Target";
        t.transform.position = pos;
        t.transform.localScale = scale;
        t.GetComponent<Renderer>().sharedMaterial = Mat(c);
        t.AddComponent<DummyTarget>();
    }

    // ------------------------------------------------------------------ Player, camera, weapon
    private static WeaponSystem BuildPlayerAndWeapon(out PlayerHealth health)
    {
        LayerMask ignorePlayer = ~(1 << PlayerLayer);

        // Camera (inactive until everything is wired)
        GameObject camGO = new GameObject("Main Camera");
        camGO.SetActive(false);
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.farClipPlane = 900f;
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0f, 3f, -6f);
        ThirdPersonCamera camRig = camGO.AddComponent<ThirdPersonCamera>();

        // Player
        GameObject player = new GameObject("Player");
        player.SetActive(false);
        player.transform.position = new Vector3(0f, 0.1f, 0f);
        playerTf = player.transform;

        GameObject body = null;
        GameObject model = null;
        GameObject charPrefab = Resources.Load<GameObject>("Characters/Ch45_nonPBR");
        if (charPrefab != null) model = AttachModel(charPrefab, player.transform, 1.8f, "Characters/Ch45_nonPBR_Diffuse");

        if (model == null)   // fallback: capsule if the Ch45 model is not in Resources
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            body.transform.localScale = Vector3.one * 0.9f;
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.2f, 0.4f, 0.8f));

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);   // shows facing direction
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(body.transform, false);
            nose.transform.localScale = new Vector3(0.4f, 0.25f, 0.4f);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 0.5f);
            nose.GetComponent<Renderer>().sharedMaterial = Mat(Color.white);
        }

        TPSCharacterController tps = player.AddComponent<TPSCharacterController>(); // adds CharacterController
        player.GetComponent<CharacterController>().radius = 0.3f;
        Set(tps, "cameraRig", camRig);
        Set(tps, "obstacleMask", ignorePlayer);
        Set(camRig, "target", player.transform);
        Set(camRig, "character", tps);
        Set(camRig, "collisionMask", ignorePlayer);

        if (body != null)
        {
            CrouchVisual cv = player.AddComponent<CrouchVisual>();
            cv.visual = body.transform;
            cv.character = tps;
        }

        // Gun + muzzle (close to the body so both hands can reach it)
        GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.name = "P90";
        Object.DestroyImmediate(gun.GetComponent<Collider>());
        gun.SetActive(false);
        gun.transform.SetParent(player.transform, false);
        gun.transform.localPosition = new Vector3(0.1f, 1.22f, 0.32f);
        gun.transform.localScale = new Vector3(0.08f, 0.14f, 0.55f);
        gun.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.15f, 0.15f, 0.17f));
        AttachP90(gun);   // swaps the cube for the real P90 model if it is in Resources/Weapons

        Transform muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(player.transform, false);
        muzzle.localPosition = new Vector3(0.1f, 1.24f, 0.68f);

        ParticleSystem flash = MakeMuzzleFlash(muzzle);
        Bullet bulletPrefab = MakeBulletPrefab();

        WeaponSystem weapon = gun.AddComponent<WeaponSystem>();
        Set(weapon, "cameraRig", camRig);
        Set(weapon, "character", tps);
        Set(weapon, "muzzle", muzzle);
        Set(weapon, "muzzleFlash", flash);
        Set(weapon, "bulletPrefab", bulletPrefab);
        Set(weapon, "hitMask", ignorePlayer);

        // Procedural animation (walk / run / crouch, both hands on the gun)
        if (model != null)
        {
            ProceduralCharacterAnimator anim = model.AddComponent<ProceduralCharacterAnimator>();
            anim.Init(player.transform, () => tps.CurrentSpeed, () => tps.IsCrouching, null, gun.transform);
        }

        health = player.AddComponent<PlayerHealth>();
        health.Init(tps, weapon);

        // Shop upgrades bought with FB Coins
        Set(weapon, "reserveAmmo", 150 + 60 * FBCoins.AmmoLevel);
        health.Upgrade(25f * FBCoins.ArmorLevel);

        SetLayerRecursive(player, PlayerLayer);

        // Activate in order: player -> camera -> gun
        player.SetActive(true);
        camGO.SetActive(true);
        gun.SetActive(true);
        return weapon;
    }

    private static ParticleSystem MakeMuzzleFlash(Transform parent)
    {
        GameObject go = new GameObject("MuzzleFlash");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.1f;
        main.startLifetime = 0.06f;
        main.startSpeed = 0f;
        main.startSize = 0.35f;
        main.startColor = new Color(1f, 0.85f, 0.3f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 8;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        go.GetComponent<ParticleSystemRenderer>().material = Unlit(Color.white);
        return ps;
    }

    private static Bullet MakeBulletPrefab()
    {
        GameObject go = new GameObject("BulletPrefab");
        go.SetActive(false);
        TrailRenderer trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.08f;
        trail.widthMultiplier = 0.05f;
        trail.startColor = new Color(1f, 0.9f, 0.4f, 1f);
        trail.endColor = new Color(1f, 0.6f, 0.1f, 0f);
        trail.material = Unlit(Color.white);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        Bullet bullet = go.AddComponent<Bullet>();
        Set(bullet, "trail", trail);
        return bullet;
    }

    // ------------------------------------------------------------------ HUD
    private static void BuildHud(WeaponSystem weapon, PlayerHealth health)
    {
        EnsureEventSystem();

        // Canvas
        GameObject canvasGO = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasGO.transform;

        MobileInput input = canvasGO.AddComponent<MobileInput>();
        Sprite circle = MakeCircle();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Look pad (right half, below the buttons)
        RectTransform lookRt = UIRect("LookPad", root, new Vector2(0.5f, 0f), Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        UIImage(lookRt, null, new Color(1f, 1f, 1f, 0.01f), true);
        TouchLookPad lookPad = lookRt.gameObject.AddComponent<TouchLookPad>();

        // Move joystick (left half)
        RectTransform moveRt = UIRect("MoveArea", root, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        UIImage(moveRt, null, new Color(1f, 1f, 1f, 0.01f), true);
        RectTransform bg = UIRect("Background", moveRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100f, -250f), new Vector2(280f, 280f));
        UIImage(bg, circle, new Color(1f, 1f, 1f, 0.25f), false);
        RectTransform handle = UIRect("Handle", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
        UIImage(handle, circle, new Color(1f, 1f, 1f, 0.6f), false);

        moveRt.gameObject.SetActive(false);   // so Awake runs after fields are set
        VirtualJoystick joystick = moveRt.gameObject.AddComponent<VirtualJoystick>();
        Set(joystick, "background", bg);
        Set(joystick, "handle", handle);
        Set(joystick, "radius", 120f);
        moveRt.gameObject.SetActive(true);

        Set(input, "moveJoystick", joystick);
        Set(input, "lookPad", lookPad);

        // Damage flash (full screen, behind the buttons, never blocks touches)
        RectTransform flashRt = UIRect("DamageFlash", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image flashImg = UIImage(flashRt, null, new Color(1f, 0f, 0f, 0f), false);

        // Health bar (top-left)
        RectTransform hbBg = UIRect("HealthBg", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(520f, 34f));
        UIImage(hbBg, null, new Color(0f, 0f, 0f, 0.5f), false);
        RectTransform hbFill = UIRect("HealthFill", hbBg, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(520f, 0f));
        UIImage(hbFill, null, new Color(0.2f, 0.85f, 0.3f, 0.95f), false);

        // Death text (hidden until the player dies)
        RectTransform deathRt = UIRect("DeathText", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 200f));
        Text deathText = deathRt.gameObject.AddComponent<Text>();
        deathText.font = font;
        deathText.fontSize = 120;
        deathText.alignment = TextAnchor.MiddleCenter;
        deathText.color = new Color(1f, 0.2f, 0.2f, 1f);
        deathText.raycastTarget = false;
        deathText.text = "YOU DIED";
        deathText.enabled = false;

        health.Bind(flashImg, hbFill, deathText);

        // FB Coins (top-center) and MENU button (top-right, under the ammo text)
        RectTransform coinRt = UIRect("Coins", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(700f, 70f));
        Text coinText = coinRt.gameObject.AddComponent<Text>();
        coinText.font = font;
        coinText.fontSize = 46;
        coinText.alignment = TextAnchor.MiddleCenter;
        coinText.color = new Color(1f, 0.85f, 0.2f);
        coinText.raycastTarget = false;
        coinRt.gameObject.AddComponent<CoinLabel>();

        Button menu = Lobby.RectButton(root, "MENU", new Vector2(1f, 1f), new Vector2(-150f, -160f), new Vector2(220f, 80f), new Color(0.2f, 0.2f, 0.25f, 0.8f), 34, font);
        menu.onClick.AddListener(() =>
            UnityEngine.SceneManagement.SceneManager.LoadScene(Mathf.Max(0, UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex)));

        // Buttons (bottom-right)
        Button fire = MakeButton("Fire", root, new Vector2(-230f, 240f), 230f, new Color(0.9f, 0.2f, 0.2f, 0.75f), circle, font, "FIRE");
        EventTrigger trig = fire.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerDown, () => weapon.StartFiring());
        AddTrigger(trig, EventTriggerType.PointerUp, () => weapon.StopFiring());
        AddTrigger(trig, EventTriggerType.PointerExit, () => weapon.StopFiring());

        Button aim = MakeButton("Aim", root, new Vector2(-500f, 160f), 150f, new Color(0.2f, 0.5f, 0.9f, 0.7f), circle, font, "AIM");
        aim.onClick.AddListener(() => weapon.ToggleAim());

        Button reload = MakeButton("Reload", root, new Vector2(-140f, 520f), 140f, new Color(0.9f, 0.7f, 0.1f, 0.7f), circle, font, "R");
        reload.onClick.AddListener(() => weapon.Reload());

        Button crouch = MakeButton("Crouch", root, new Vector2(-440f, 400f), 140f, new Color(0.3f, 0.7f, 0.3f, 0.7f), circle, font, "CROUCH");
        crouch.onClick.AddListener(() => input.ToggleCrouch());

        // Crosshair
        RectTransform ch = UIRect("Crosshair", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        UIImage(ch, circle, new Color(1f, 1f, 1f, 0.9f), false);

        // Ammo text
        RectTransform ammoRt = UIRect("Ammo", root, Vector2.one, Vector2.one, Vector2.one, new Vector2(-40f, -40f), new Vector2(500f, 90f));
        Text ammo = ammoRt.gameObject.AddComponent<Text>();
        ammo.font = font;
        ammo.fontSize = 56;
        ammo.alignment = TextAnchor.UpperRight;
        ammo.color = Color.white;
        ammo.raycastTarget = false;
        weapon.AmmoChanged += (m, r) => ammo.text = m + " / " + r;
    }

    private static Button MakeButton(string name, Transform parent, Vector2 pos, float size, Color color, Sprite circle, Font font, string label)
    {
        RectTransform rt = UIRect(name, parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
        Image img = UIImage(rt, circle, color, true);
        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;

        RectTransform tr = UIRect("Label", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Text t = tr.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = label;
        t.fontSize = 32;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        return b;
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityAction action)
    {
        EventTrigger.Entry e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(_ => action());
        trigger.triggers.Add(e);
    }

    internal static RectTransform UIRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    internal static Image UIImage(RectTransform rt, Sprite sprite, Color color, bool raycast)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        img.canvasRenderer.cullTransparentMesh = false;
        return img;
    }

    internal static Sprite MakeCircle()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------------ Models (Resources)
    /// <summary>Builds a city grid from Resources/City/building-*.fbx (auto-fitted, box colliders).</summary>
    private static bool TryBuildCity()
    {
        GameObject[] all = Resources.LoadAll<GameObject>("City");
        List<GameObject> buildings = new List<GameObject>();
        List<GameObject> props = new List<GameObject>();
        foreach (GameObject g in all)
        {
            if (g.name.StartsWith("building")) buildings.Add(g);
            else if (g.name.StartsWith("shipping") || g.name.StartsWith("detail-tank") || g.name.StartsWith("water-tower")) props.Add(g);
        }
        if (buildings.Count == 0) return false;

        Transform root = new GameObject("City").transform;
        System.Random rng = new System.Random(7);
        const float spacing = 26f;
        const float footprint = 18f;

        for (int i = -3; i <= 3; i++)
        {
            if (i == 0) continue;   // keep a wide avenue in the middle
            for (int j = 1; j <= 5; j++)
            {
                GameObject prefab = buildings[rng.Next(buildings.Count)];
                PlaceModel(prefab, new Vector3(i * spacing, 0f, j * spacing), rng.Next(4) * 90f, footprint, root);
            }
        }

        if (props.Count > 0)
        {
            for (int k = 0; k < 8; k++)
            {
                GameObject prefab = props[rng.Next(props.Count)];
                float x = (k % 2 == 0 ? -1f : 1f) * (11f + (float)rng.NextDouble() * 3f);
                PlaceModel(prefab, new Vector3(x, 0f, 18f + k * 9f), rng.Next(4) * 90f, 5f, root);
            }
        }
        return true;
    }

    private static void PlaceModel(GameObject prefab, Vector3 pos, float yRot, float footprint, Transform parent)
    {
        GameObject go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        Bounds b = GetBounds(go);
        float size = Mathf.Max(b.size.x, b.size.z);
        if (size > 0.001f) go.transform.localScale *= footprint / size;

        b = GetBounds(go);
        go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);

        Bounds fb = GetBounds(go);
        BoxCollider box = go.AddComponent<BoxCollider>();
        Vector3 ls = go.transform.lossyScale;
        box.center = go.transform.InverseTransformPoint(fb.center);
        box.size = new Vector3(fb.size.x / ls.x, fb.size.y / ls.y, fb.size.z / ls.z);

        go.transform.RotateAround(pos, Vector3.up, yRot);
        go.transform.SetParent(parent, true);
    }

    // ------------------------------------------------------------------ Props: trees, cars, clouds, sun
    private static readonly Dictionary<string, Material> texMats = new Dictionary<string, Material>();
    private static readonly string[] TreeNames = { "01", "02", "03", "04", "05", "06", "08", "11", "12", "13", "47", "48", "49", "52", "54", "55", "59", "60" };

    private static void BuildProps()
    {
        System.Random rng = new System.Random(21);
        Transform root = new GameObject("Props").transform;

        // ---- Trees (one FBX holds ~200 single-mesh trees) ----
        GameObject treesPrefab = Resources.Load<GameObject>("Trees/LowPoly_Tree_Collection_01_fbx");
        if (treesPrefab != null)
        {
            GameObject master;
            List<Transform> trees = ExtractChildren(treesPrefab, n => System.Array.IndexOf(TreeNames, n) >= 0, out master);
            if (trees.Count == 0) trees = ExtractChildren(treesPrefab, n => true, out master);
            if (trees.Count > 0)
            {
                for (int i = 0; i < 24; i++)   // along both sides of the avenue
                {
                    float side = (i % 2 == 0) ? -1f : 1f;
                    float x = side * (15.2f + (float)rng.NextDouble() * 1.2f);
                    float z = 24f + (i / 2) * 10f + (float)rng.NextDouble() * 3f;
                    PlaceProp(trees[rng.Next(trees.Count)].gameObject, new Vector3(x, 0f, z), rng.Next(360), 6f + (float)rng.NextDouble() * 3f, true, false, false, root, "Trees/Trees_color");
                }
                for (int i = 0; i < 14; i++)   // around the spawn plaza
                {
                    float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float rad = 14f + (float)rng.NextDouble() * 12f;
                    Vector3 p = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad - 4f);
                    PlaceProp(trees[rng.Next(trees.Count)].gameObject, p, rng.Next(360), 6f + (float)rng.NextDouble() * 3f, true, false, false, root, "Trees/Trees_color");
                }
            }
            if (master != null) Object.Destroy(master);
        }

        // ---- Parked vehicles (one FBX holds cars, trucks and tractors) ----
        GameObject carsPrefab = Resources.Load<GameObject>("Cars/LowPoly_Cars_01_fbx");
        if (carsPrefab != null)
        {
            GameObject master;
            List<Transform> vehicles = ExtractChildren(carsPrefab, n => n.StartsWith("car_") || n.StartsWith("truck_") || n.StartsWith("tractor_"), out master);
            if (vehicles.Count > 0)
            {
                for (int i = 0; i < 14; i++)
                {
                    Transform v = vehicles[rng.Next(vehicles.Count)];
                    float len = v.name.StartsWith("truck_") ? 7.5f : (v.name.StartsWith("tractor_") ? 4.2f : 4.4f);
                    float side = (i % 2 == 0) ? -1f : 1f;
                    float x = side * (11.5f + (float)rng.NextDouble() * 2.5f);
                    float z = 32f + i * 8f + (float)rng.NextDouble() * 3f;
                    float yaw = rng.Next(2) * 180f + ((float)rng.NextDouble() - 0.5f) * 6f;
                    PlaceProp(v.gameObject, new Vector3(x, 0f, z), yaw, len, false, true, true, root, "Cars/Cars_color");
                }
            }
            if (master != null) Object.Destroy(master);
        }

        // ---- R8 sports car near the spawn ----
        GameObject r8 = Resources.Load<GameObject>("Cars/r8");
        if (r8 != null) PlaceProp(r8, new Vector3(9f, 0f, 10f), 25f, 4.5f, false, true, true, root, null);

        // ---- Sky: clouds + sun (ignore-raycast layer so bullets pass through) ----
        GameObject cloud = Resources.Load<GameObject>("Sky/cloud");
        if (cloud != null)
        {
            for (int i = 0; i < 10; i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * 300f;
                float z = -40f + (float)rng.NextDouble() * 280f;
                GameObject c = PlaceProp(cloud, new Vector3(x, 0f, z), rng.Next(360), 45f + (float)rng.NextDouble() * 35f, false, false, false, root, null);
                c.transform.position += Vector3.up * (70f + (float)rng.NextDouble() * 40f);
                SetLayerRecursive(c, 2);
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        GameObject sunPrefab = Resources.Load<GameObject>("Sky/Sun");
        if (sunPrefab != null)
        {
            Light dl = Object.FindObjectOfType<Light>();
            Vector3 dir = dl != null ? -dl.transform.forward : new Vector3(-0.4f, 0.7f, -0.5f).normalized;
            GameObject sun = PlaceProp(sunPrefab, Vector3.zero, 0f, 90f, false, false, false, root, null);
            Bounds sb = GetBounds(sun);
            sun.transform.position += dir * 600f - sb.center;
            SetLayerRecursive(sun, 2);
            foreach (Renderer r in sun.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>Clones a model from the Resources prefab and returns its direct children that pass the filter.</summary>
    private static List<Transform> ExtractChildren(GameObject prefab, System.Predicate<string> filter, out GameObject master)
    {
        master = Object.Instantiate(prefab, new Vector3(3000f, 0f, 3000f), Quaternion.identity);
        List<Transform> list = new List<Transform>();
        foreach (Transform t in master.transform)
            if (filter(t.name)) list.Add(t);
        return list;
    }

    /// <summary>
    /// Clones 'source', fits it to 'target' metres (length or height), puts its base on the ground at 'pos', turns it by yRot.
    /// Camera/Light components that come with Blender exports are removed.
    /// </summary>
    private static GameObject PlaceProp(GameObject source, Vector3 pos, float yRot, float target, bool byHeight, bool alignZ, bool collider, Transform parent, string texPath)
    {
        GameObject go = Object.Instantiate(source);
        foreach (Camera c in go.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        foreach (AudioListener a in go.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(a);
        if (!string.IsNullOrEmpty(texPath)) ApplySharedTexture(go, texPath);

        Bounds b = GetBounds(go);
        if (alignZ && b.size.x > b.size.z)
        {
            go.transform.Rotate(0f, 90f, 0f, Space.World);
            b = GetBounds(go);
        }
        float size = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
        if (size > 0.0001f) go.transform.localScale *= target / size;

        b = GetBounds(go);
        go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);

        if (collider)
        {
            Bounds fb = GetBounds(go);
            BoxCollider box = go.AddComponent<BoxCollider>();
            Vector3 ls = go.transform.lossyScale;
            box.center = go.transform.InverseTransformPoint(fb.center);
            box.size = new Vector3(fb.size.x / ls.x, fb.size.y / ls.y, fb.size.z / ls.z);
        }

        go.transform.RotateAround(pos, Vector3.up, yRot);
        go.transform.SetParent(parent, true);
        return go;
    }

    /// <summary>One shared material per texture (keeps draw calls low for props).</summary>
    private static void ApplySharedTexture(GameObject go, string path)
    {
        Material m;
        if (!texMats.TryGetValue(path, out m) || m == null)
        {
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex == null) return;
            m = Mat(Color.white);
            m.mainTexture = tex;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            texMats[path] = m;
        }
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            Material[] arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = m;
            r.sharedMaterials = arr;
        }
    }

    /// <summary>
    /// Instantiates a Mixamo model under 'parent': scaled to 'height' metres, feet on the parent origin,
    /// turned to face +Z (detected from the hand bones) and textured from Resources (embedded FBX textures are unreliable).
    /// </summary>
    internal static GameObject AttachModel(GameObject prefab, Transform parent, float height, string texturePath)
    {
        GameObject m = Object.Instantiate(prefab, new Vector3(1000f, 0f, 1000f), Quaternion.identity);
        foreach (Animator a in m.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(a); // we animate by code

        Transform lh = null, rh = null;
        foreach (Transform t in m.GetComponentsInChildren<Transform>())
        {
            if (t.name.EndsWith("LeftHand")) lh = t;
            else if (t.name.EndsWith("RightHand")) rh = t;
        }
        Quaternion faceFix = Quaternion.identity;
        if (lh != null && rh != null)
        {
            Vector3 right = rh.position - lh.position;
            right.y = 0f;
            if (right.sqrMagnitude > 0.0001f)
            {
                Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
                faceFix = Quaternion.Euler(0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), 0f);
            }
        }
        m.transform.rotation = faceFix;

        Bounds b = GetBounds(m);
        if (b.size.y < 0.05f) { Object.Destroy(m); return null; }
        float s = height / b.size.y;
        float footOffset = b.min.y - m.transform.position.y;
        Vector3 baseScale = m.transform.localScale;

        m.name = "CharacterModel";
        m.transform.SetParent(parent, false);
        m.transform.localRotation = faceFix;
        m.transform.localScale = baseScale * s;
        m.transform.localPosition = new Vector3(0f, -footOffset * s, 0f);

        ApplyTexture(m, texturePath);
        return m;
    }

    private static void ApplyTexture(GameObject go, string path)
    {
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null) return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i].mainTexture = tex;
                mats[i].color = Color.white;
            }
            r.materials = mats;
        }
    }

    // ------------------------------------------------------------------ Enemies
    private static void SpawnEnemies(Transform player, PlayerHealth health)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0 || player == null) return;

        Vector3[] spots =
        {
            new Vector3(6f, 0.1f, 40f), new Vector3(-8f, 0.1f, 55f), new Vector3(10f, 0.1f, 75f),
            new Vector3(-5f, 0.1f, 95f), new Vector3(12f, 0.1f, 115f), new Vector3(-12f, 0.1f, 70f)
        };

        for (int i = 0; i < spots.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i % enemyPrefabs.Length];
            string n = prefab.name;
            bool warrok = n.StartsWith("Warrok");
            bool mutant = n.StartsWith("Mutant");
            float h = warrok ? 2.4f : (mutant ? 2.1f : 1.85f);
            float speed = warrok ? 2.4f : (mutant ? 3.0f : 3.4f);

            GameObject e = new GameObject("Enemy_" + n);
            e.SetActive(false);
            e.transform.position = spots[i];

            string texName = n.Replace(' ', '_').Replace('.', '_');   // FBX names on GitHub may have spaces/dots
            GameObject model = AttachModel(prefab, e.transform, h, "Enemies/" + texName + "_Diffuse");
            if (model == null) { Object.Destroy(e); continue; }

            CharacterController cc = e.AddComponent<CharacterController>();
            cc.height = h;
            cc.radius = Mathf.Clamp(h * 0.2f, 0.35f, 0.55f);
            cc.center = new Vector3(0f, h * 0.5f, 0f);
            cc.stepOffset = 0.3f;

            EnemyAI ai = e.AddComponent<EnemyAI>();
            ai.player = player;
            ai.playerHealth = health;
            ai.moveSpeed = speed;
            ai.spawnPoint = spots[i];
            ai.coinReward = warrok ? 40 : (mutant ? 30 : 20);

            ProceduralCharacterAnimator anim = model.AddComponent<ProceduralCharacterAnimator>();
            anim.Init(e.transform, () => ai.CurrentSpeed, null, () => ai.AttackAnim, null);

            SetLayerRecursive(e, EnemyLayer);
            e.SetActive(true);
        }
    }

    /// <summary>Replaces the cube gun with Resources/Weapons/P90, auto-detecting which end is the barrel.</summary>
    private static void AttachP90(GameObject gun)
    {
        GameObject prefab = Resources.Load<GameObject>("Weapons/P90");
        if (prefab == null) return;

        GameObject m = Object.Instantiate(prefab, new Vector3(1000f, 0f, 1000f), Quaternion.identity);
        foreach (Animator a in m.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(a);

        // Collect vertices in the model's local space (needs Read/Write: see Assets/Editor/ModelImportSettings.cs)
        List<Vector3> pts = new List<Vector3>();
        foreach (MeshFilter mf in m.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Vector3[] vs = mesh.vertices;
            Transform t = mf.transform;
            for (int i = 0; i < vs.Length; i++) pts.Add(m.transform.InverseTransformPoint(t.TransformPoint(vs[i])));
        }

        Quaternion r = Quaternion.identity;
        Vector3 center = Vector3.zero;
        float length = 1f;

        if (pts.Count > 0)
        {
            Vector3 mn = pts[0], mx = pts[0];
            foreach (Vector3 p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 size = mx - mn;
            center = (mn + mx) * 0.5f;

            int axis = size.x >= size.z ? 0 : 2;   // gun lies horizontally: barrel is along X or Z
            length = size[axis];
            float cut = length * 0.15f;
            Vector3 loMin = Vector3.one * 1e9f, loMax = Vector3.one * -1e9f, hiMin = loMin, hiMax = loMax;
            foreach (Vector3 p in pts)
            {
                float a = p[axis] - mn[axis];
                if (a < cut) { loMin = Vector3.Min(loMin, p); loMax = Vector3.Max(loMax, p); }
                else if (a > length - cut) { hiMin = Vector3.Min(hiMin, p); hiMax = Vector3.Max(hiMax, p); }
            }
            int o1 = axis == 0 ? 1 : 0;
            int o2 = axis == 0 ? 2 : 1;
            float areaLo = (loMax[o1] - loMin[o1]) * (loMax[o2] - loMin[o2]);
            float areaHi = (hiMax[o1] - hiMin[o1]) * (hiMax[o2] - hiMin[o2]);
            float sign = areaHi < areaLo ? 1f : -1f;   // thin end = barrel
            Vector3 barrelDir = axis == 0 ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
            r = Quaternion.Inverse(Quaternion.LookRotation(barrelDir, Vector3.up));
        }
        else
        {
            Bounds b = GetBounds(m);
            Vector3 lp = m.transform.InverseTransformPoint(b.center);
            center = lp;
            length = Mathf.Max(b.size.x, b.size.z) / Mathf.Max(0.0001f, m.transform.lossyScale.x);
            if (b.size.x > b.size.z) r = Quaternion.Euler(0f, -90f, 0f);
            Debug.LogWarning("GameBootstrap: P90 mesh is not readable, barrel direction is a guess.");
        }

        float s = 0.62f / Mathf.Max(0.0001f, length);   // P90 is about 0.6 m long
        gun.transform.localScale = Vector3.one;
        Object.Destroy(gun.GetComponent<MeshRenderer>());
        Object.Destroy(gun.GetComponent<MeshFilter>());

        m.name = "P90Model";
        m.transform.SetParent(gun.transform, false);
        m.transform.localRotation = r;
        m.transform.localScale = Vector3.one * s;
        m.transform.localPosition = -(r * (center * s));

        // P90 textures are not part of the FBX, so use a plain dark metal look
        Material dark = Mat(new Color(0.13f, 0.13f, 0.15f));
        foreach (Renderer rend in m.GetComponentsInChildren<Renderer>())
        {
            Material[] arr = new Material[rend.sharedMaterials.Length];
            for (int k = 0; k < arr.Length; k++) arr[k] = dark;
            rend.sharedMaterials = arr;
        }
    }

    internal static Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // ------------------------------------------------------------------ Helpers
    internal static Material Mat(Color c)
    {
        if (baseMat == null)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMat = p.GetComponent<Renderer>().sharedMaterial;   // works in Built-in and URP
            Object.Destroy(p);
        }
        Material m = new Material(baseMat);
        m.color = c;
        return m;
    }

    internal static Material Unlit(Color c)
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) return Mat(c);
        Material m = new Material(s);
        m.color = c;
        return m;
    }

    internal static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    /// <summary>Sets a [SerializeField] private field by name (our scripts keep their fields private).</summary>
    internal static void Set(object target, string field, object value)
    {
        FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) { Debug.LogError("GameBootstrap: field '" + field + "' not found on " + target.GetType().Name); return; }
        f.SetValue(target, value);
    }
}

/// <summary>Target dummy: takes damage, flashes red, disappears when dead, respawns after 3s.</summary>
public class DummyTarget : MonoBehaviour, IDamageable
{
    private float hp = 100f;
    private Renderer rend;
    private Collider col;
    private Color baseColor;
    private float flashUntil;
    private float respawnAt = -1f;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        col = GetComponent<Collider>();
        baseColor = rend.material.color;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (respawnAt > 0f) return;
        hp -= amount;
        rend.material.color = Color.red;
        flashUntil = Time.time + 0.08f;
        if (hp <= 0f)
        {
            respawnAt = Time.time + 3f;
            rend.enabled = false;
            col.enabled = false;
        }
    }

    private void Update()
    {
        if (flashUntil > 0f && Time.time >= flashUntil)
        {
            rend.material.color = baseColor;
            flashUntil = 0f;
        }
        if (respawnAt > 0f && Time.time >= respawnAt)
        {
            respawnAt = -1f;
            hp = 100f;
            rend.enabled = true;
            col.enabled = true;
        }
    }
}

/// <summary>Shrinks the capsule body while crouching (no animator needed for the test character).</summary>
public class CrouchVisual : MonoBehaviour
{
    public Transform visual;
    public TPSCharacterController character;

    private void Update()
    {
        if (visual == null || character == null) return;
        float target = character.IsCrouching ? 0.6f : 0.9f;
        float s = Mathf.MoveTowards(visual.localScale.y, target, 2f * Time.deltaTime);
        visual.localScale = new Vector3(0.9f, s, 0.9f);
        visual.localPosition = new Vector3(0f, s, 0f);
    }
}


/// <summary>Player health: damage flash, health bar, death + respawn after 3 seconds.</summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    public bool IsDead { get; private set; }

    private float hp;
    private float respawnAt;
    private float flashAlpha;
    private Image flash;
    private RectTransform barFill;
    private float barFullWidth = 520f;
    private Text deathText;
    private TPSCharacterController tps;
    private WeaponSystem weapon;
    private CharacterController cc;
    private Vector3 spawn;

    public void Init(TPSCharacterController controller, WeaponSystem w)
    {
        tps = controller;
        weapon = w;
        cc = GetComponent<CharacterController>();
        spawn = transform.position;
        hp = maxHealth;
    }

    public void Upgrade(float extraMax)
    {
        if (extraMax <= 0f) return;
        maxHealth += extraMax;
        hp = maxHealth;
        UpdateBar();
    }

    public void Bind(Image flashImage, RectTransform fill, Text death)
    {
        flash = flashImage;
        barFill = fill;
        deathText = death;
        barFullWidth = fill.sizeDelta.x;
        UpdateBar();
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        hp = Mathf.Max(0f, hp - amount);
        flashAlpha = 0.45f;
        UpdateBar();
        if (hp <= 0f) Die();
    }

    private void Die()
    {
        IsDead = true;
        respawnAt = Time.time + 3f;
        weapon.StopFiring();
        weapon.enabled = false;
        tps.enabled = false;
        if (deathText != null) deathText.enabled = true;
    }

    private void Respawn()
    {
        cc.enabled = false;
        transform.position = spawn;
        cc.enabled = true;
        hp = maxHealth;
        IsDead = false;
        tps.enabled = true;
        weapon.enabled = true;
        if (deathText != null) deathText.enabled = false;
        UpdateBar();
    }

    private void UpdateBar()
    {
        if (barFill != null) barFill.sizeDelta = new Vector2(barFullWidth * (hp / maxHealth), 0f);
    }

    private void Update()
    {
        if (flash != null && flashAlpha > 0f)
        {
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 1.5f);
            Color c = flash.color;
            c.a = flashAlpha;
            flash.color = c;
        }
        if (IsDead && Time.time >= respawnAt) Respawn();
    }
}

/// <summary>Melee enemy: chases the player, hits when close, can be shot, falls over, respawns after 6 seconds.</summary>
public class EnemyAI : MonoBehaviour, IDamageable
{
    public Transform player;
    public PlayerHealth playerHealth;
    public Vector3 spawnPoint;
    public float moveSpeed = 3f;
    public float detectRange = 45f;
    public float attackRange = 1.9f;
    public float attackDamage = 10f;
    public float attackCooldown = 1.2f;
    public float maxHealth = 100f;
    public int coinReward = 20;

    public float CurrentSpeed { get; private set; }
    public float AttackAnim { get; private set; }

    private static readonly float[] SteerAngles = { 0f, 40f, -40f, 80f, -80f, 120f, -120f };

    private CharacterController cc;
    private readonly List<Material> mats = new List<Material>();
    private float hp;
    private float vy;
    private float nextAttack;
    private float aggroUntil;
    private float flashUntil;
    private bool dead;
    private float diedAt;
    private float respawnAt;
    private Quaternion deathStartRot;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        hp = maxHealth;
    }

    private void Start()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            foreach (Material m in r.materials) mats.Add(m);
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (dead) return;
        hp -= amount;
        aggroUntil = Time.time + 10f;
        Tint(Color.red);
        flashUntil = Time.time + 0.08f;
        if (hp <= 0f) Die();
    }

    private void Die()
    {
        dead = true;
        diedAt = Time.time;
        respawnAt = Time.time + 6f;
        deathStartRot = transform.rotation;
        CurrentSpeed = 0f;
        cc.enabled = false;
        FBCoins.Add(coinReward);
    }

    private void Respawn()
    {
        transform.SetPositionAndRotation(spawnPoint, Quaternion.identity);
        cc.enabled = true;
        hp = maxHealth;
        dead = false;
        vy = 0f;
        Tint(Color.white);
    }

    private void Tint(Color c)
    {
        for (int i = 0; i < mats.Count; i++) mats[i].color = c;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (dead)
        {
            float t = Mathf.Clamp01((Time.time - diedAt) / 0.6f);
            transform.rotation = Quaternion.Slerp(deathStartRot, deathStartRot * Quaternion.Euler(90f, 0f, 0f), t);
            if (Time.time >= respawnAt) Respawn();
            return;
        }
        if (player == null) return;

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        Vector3 to = player.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        bool aggro = !playerDead && (dist < detectRange || Time.time < aggroUntil);

        Vector3 move = Vector3.zero;
        float targetSpeed = 0f;

        if (aggro && dist > attackRange)
        {
            Vector3 dir = Steer(to / Mathf.Max(dist, 0.001f));
            move = dir * moveSpeed;
            targetSpeed = moveSpeed;
            Face(dir, dt);
        }
        else if (aggro)
        {
            Face(to.normalized, dt);
            if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + attackCooldown;
                AttackAnim = 1f;
                if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
            }
        }

        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, 12f * dt);
        AttackAnim = Mathf.MoveTowards(AttackAnim, 0f, 2.5f * dt);

        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += -25f * dt;
        cc.Move((move + Vector3.up * vy) * dt);

        if (flashUntil > 0f && Time.time >= flashUntil)
        {
            Tint(Color.white);
            flashUntil = 0f;
        }
    }

    private void Face(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 360f * dt);
    }

    /// <summary>Simple wall avoidance: try the direct way first, then widening angles.</summary>
    private Vector3 Steer(Vector3 dir)
    {
        Vector3 origin = transform.position + Vector3.up * 0.9f;
        int mask = ~((1 << 8) | (1 << 9));   // ignore player + enemy layers
        for (int i = 0; i < SteerAngles.Length; i++)
        {
            Vector3 d = Quaternion.Euler(0f, SteerAngles[i], 0f) * dir;
            if (!Physics.SphereCast(origin, 0.35f, d, out RaycastHit hit, 2.5f, mask, QueryTriggerInteraction.Ignore)) return d;
        }
        return dir;
    }
}

/// <summary>
/// Code-driven animation for Mixamo skeletons (no Animator / clips needed):
/// idle, walk, run, crouch, arms swinging, enemy attack swing, or both hands on a gun via 2-bone IK.
/// </summary>
public class ProceduralCharacterAnimator : MonoBehaviour
{
    private class Bone
    {
        public Transform t;
        public Quaternion restLocal;
        public Vector3 restLocalPos;
    }

    private Bone hips, spine, lUp, lLeg, lFoot, rUp, rLeg, rFoot, lArm, lFore, lHand, rArm, rFore, rHand;
    private Transform root, gun;
    private System.Func<float> speedGet;
    private System.Func<bool> crouchGet;
    private System.Func<float> attackGet;
    private Quaternion lFootRest, rFootRest;
    private Vector3 lArmRestDir, rArmRestDir;
    private float phase, crouchT, smoothSpeed;
    private bool ready;
    private readonly List<Bone> all = new List<Bone>();

    public void Init(Transform rootTransform, System.Func<float> speed, System.Func<bool> crouching, System.Func<float> attack, Transform gunHold)
    {
        root = rootTransform;
        speedGet = speed;
        crouchGet = crouching;
        attackGet = attack;
        gun = gunHold;

        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int i = n.LastIndexOf(':');
            if (i >= 0) n = n.Substring(i + 1);
            if (!map.ContainsKey(n)) map.Add(n, t);
        }

        hips = Make(map, "Hips");
        spine = Make(map, "Spine");
        lUp = Make(map, "LeftUpLeg");
        lLeg = Make(map, "LeftLeg");
        lFoot = Make(map, "LeftFoot");
        rUp = Make(map, "RightUpLeg");
        rLeg = Make(map, "RightLeg");
        rFoot = Make(map, "RightFoot");
        lArm = Make(map, "LeftArm");
        lFore = Make(map, "LeftForeArm");
        lHand = Make(map, "LeftHand");
        rArm = Make(map, "RightArm");
        rFore = Make(map, "RightForeArm");
        rHand = Make(map, "RightHand");

        ready = hips != null && spine != null && lUp != null && lLeg != null && lFoot != null && rUp != null && rLeg != null
                && rFoot != null && lArm != null && lFore != null && lHand != null && rArm != null && rFore != null && rHand != null;
        if (!ready)
        {
            Debug.LogWarning("ProceduralCharacterAnimator: skeleton bones not found on " + name);
            return;
        }

        lFootRest = Quaternion.Inverse(root.rotation) * lFoot.t.rotation;
        rFootRest = Quaternion.Inverse(root.rotation) * rFoot.t.rotation;
        lArmRestDir = root.InverseTransformDirection((lFore.t.position - lArm.t.position).normalized);
        rArmRestDir = root.InverseTransformDirection((rFore.t.position - rArm.t.position).normalized);
    }

    private Bone Make(Dictionary<string, Transform> map, string key)
    {
        Transform t;
        if (!map.TryGetValue(key, out t)) return null;
        Bone b = new Bone { t = t, restLocal = t.localRotation, restLocalPos = t.localPosition };
        all.Add(b);
        return b;
    }

    private static void Rot(Bone b, float angleDeg, Vector3 axis)
    {
        b.t.rotation = Quaternion.AngleAxis(angleDeg, axis) * b.t.rotation;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

        float speed = speedGet != null ? speedGet() : 0f;
        bool crouching = crouchGet != null && crouchGet();
        float attack = attackGet != null ? attackGet() : 0f;
        smoothSpeed = Mathf.MoveTowards(smoothSpeed, speed, 12f * dt);
        crouchT = Mathf.MoveTowards(crouchT, crouching ? 1f : 0f, 5f * dt);

        // Back to the rest (T) pose, then build the pose on top of it
        for (int i = 0; i < all.Count; i++) all[i].t.localRotation = all[i].restLocal;
        hips.t.localPosition = hips.restLocalPos;

        Vector3 right = root.right;

        float freq = smoothSpeed / (1f + smoothSpeed * 0.25f);
        phase += freq * dt;
        float s = Mathf.Sin(phase * 6.2832f);
        float c = Mathf.Cos(phase * 6.2832f);
        float moveFactor = Mathf.Clamp01(smoothSpeed / 1.5f);
        float amp = Mathf.Clamp(smoothSpeed * 18f, 0f, 55f) * (1f - 0.4f * crouchT);
        float bendAmp = Mathf.Clamp(smoothSpeed * 20f, 0f, 90f);

        // Body: crouch drop + forward lean (positive angle about "right" leans forward)
        if (crouchT > 0.001f) hips.t.position += Vector3.down * (0.3f * crouchT);
        Rot(spine, 8f * Mathf.Clamp01((smoothSpeed - 3f) / 2f) + 12f * crouchT, right);

        // Legs: negative angle about "right" = swing forward, positive knee angle = heel back
        float fL = s * amp;
        float fR = -s * amp;
        Rot(lUp, -(fL + 60f * crouchT), right);
        Rot(lLeg, Mathf.Max(0f, c) * bendAmp * moveFactor + 95f * crouchT, right);
        Rot(rUp, -(fR + 60f * crouchT), right);
        Rot(rLeg, Mathf.Max(0f, -c) * bendAmp * moveFactor + 95f * crouchT, right);

        lFoot.t.rotation = root.rotation * lFootRest;   // keep feet flat
        rFoot.t.rotation = root.rotation * rFootRest;

        // Arms
        if (gun != null)
        {
            HoldGun(lArm, lFore, lHand);
            HoldGun(rArm, rFore, rHand);
        }
        else
        {
            float swing = amp * 0.8f;
            float bend = 15f + 25f * moveFactor + 10f * attack;
            Hang(lArm, lFore, lArmRestDir, -s * swing + attack * 85f, bend);
            Hang(rArm, rFore, rArmRestDir, s * swing + attack * 85f, bend);
        }
    }

    private void Hang(Bone arm, Bone fore, Vector3 restDirLocal, float swingForwardDeg, float foreBendDeg)
    {
        Vector3 curDir = (fore.t.position - arm.t.position).normalized;
        float side = restDirLocal.x >= 0f ? 1f : -1f;
        Vector3 hangWorld = root.TransformDirection(new Vector3(side * 0.18f, -1f, 0.05f).normalized);
        arm.t.rotation = Quaternion.FromToRotation(curDir, hangWorld) * arm.t.rotation;
        arm.t.rotation = Quaternion.AngleAxis(-swingForwardDeg, root.right) * arm.t.rotation;
        fore.t.rotation = Quaternion.AngleAxis(-foreBendDeg, root.right) * fore.t.rotation;
    }

    private void HoldGun(Bone arm, Bone fore, Bone hand)
    {
        float side = Mathf.Sign(Vector3.Dot(root.right, arm.t.position - hips.t.position));
        Vector3 target = side > 0f
            ? gun.TransformPoint(new Vector3(0f, -0.09f, -0.03f))   // right hand: grip
            : gun.TransformPoint(new Vector3(0f, -0.05f, 0.2f));    // left hand: front hand guard
        Vector3 pole = root.TransformDirection(new Vector3(side * 0.6f, -0.7f, -0.35f));
        SolveIK(arm.t, fore.t, hand.t, target, pole);
    }

    private static void SolveIK(Transform upper, Transform mid, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 a = upper.position;
        Vector3 b = mid.position;
        Vector3 c = end.position;
        float l1 = (b - a).magnitude;
        float l2 = (c - b).magnitude;

        Vector3 toTarget = target - a;
        float dist = Mathf.Clamp(toTarget.magnitude, 0.05f, (l1 + l2) * 0.999f);
        Vector3 dir = toTarget.normalized;

        float d1 = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - d1 * d1));
        Vector3 side = Vector3.ProjectOnPlane(pole, dir);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.up;
        Vector3 elbow = a + dir * d1 + side * h;

        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        Vector3 mp = mid.position;
        mid.rotation = Quaternion.FromToRotation(end.position - mp, target - mp) * mid.rotation;
    }
}
