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
    private static Material baseMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Object.FindObjectOfType<TPSCharacterController>() != null) return;

        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        BuildWorld();
        WeaponSystem weapon = BuildPlayerAndWeapon();
        BuildHud(weapon);
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

        MakeTarget(new Vector3(0f, 1f, 14f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.9f, 0.3f, 0.2f));
        MakeTarget(new Vector3(-4f, 1f, 20f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.9f, 0.5f, 0.1f));
        MakeTarget(new Vector3(4f, 0.75f, 22f), PrimitiveType.Cube, new Vector3(1.5f, 1.5f, 1.5f), new Color(0.8f, 0.2f, 0.5f));
        MakeTarget(new Vector3(10f, 1f, 8f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.2f, 0.5f, 0.9f));
        MakeTarget(new Vector3(-10f, 1f, 6f), PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.6f, 0.2f, 0.8f));
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
    private static WeaponSystem BuildPlayerAndWeapon()
    {
        LayerMask ignorePlayer = ~(1 << PlayerLayer);

        // Camera (inactive until everything is wired)
        GameObject camGO = new GameObject("Main Camera");
        camGO.SetActive(false);
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.farClipPlane = 300f;
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0f, 3f, -6f);
        ThirdPersonCamera camRig = camGO.AddComponent<ThirdPersonCamera>();

        // Player
        GameObject player = new GameObject("Player");
        player.SetActive(false);
        player.transform.position = new Vector3(0f, 0.1f, 0f);

        GameObject body = null;
        if (!TryAttachCharacter(player))   // fallback: capsule if Ch45 model is not in Resources
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

        // Gun + muzzle
        GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.name = "P90";
        Object.DestroyImmediate(gun.GetComponent<Collider>());
        gun.SetActive(false);
        gun.transform.SetParent(player.transform, false);
        gun.transform.localPosition = new Vector3(0.28f, 1.25f, 0.45f);
        gun.transform.localScale = new Vector3(0.08f, 0.14f, 0.55f);
        gun.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.15f, 0.15f, 0.17f));
        AttachP90(gun);   // swaps the cube for the real P90 model if it is in Resources/Weapons

        Transform muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(player.transform, false);
        muzzle.localPosition = new Vector3(0.28f, 1.27f, 0.8f);

        ParticleSystem flash = MakeMuzzleFlash(muzzle);
        Bullet bulletPrefab = MakeBulletPrefab();

        WeaponSystem weapon = gun.AddComponent<WeaponSystem>();
        Set(weapon, "cameraRig", camRig);
        Set(weapon, "character", tps);
        Set(weapon, "muzzle", muzzle);
        Set(weapon, "muzzleFlash", flash);
        Set(weapon, "bulletPrefab", bulletPrefab);
        Set(weapon, "hitMask", ignorePlayer);

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
    private static void BuildHud(WeaponSystem weapon)
    {
        // Event system
        GameObject es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.InputSystem.UI.InputSystemUIInputModule module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        module.AssignDefaultActions();
#else
        es.AddComponent<StandaloneInputModule>();
#endif

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

    private static RectTransform UIRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
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

    private static Image UIImage(RectTransform rt, Sprite sprite, Color color, bool raycast)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        img.canvasRenderer.cullTransparentMesh = false;
        return img;
    }

    private static Sprite MakeCircle()
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

    /// <summary>Puts the Mixamo Ch45 model (Resources/Characters/Ch45_nonPBR) on the player, 1.8 m tall, facing +Z.</summary>
    private static bool TryAttachCharacter(GameObject player)
    {
        GameObject prefab = Resources.Load<GameObject>("Characters/Ch45_nonPBR");
        if (prefab == null) return false;

        GameObject m = Object.Instantiate(prefab, new Vector3(1000f, 0f, 1000f), Quaternion.identity);
        foreach (Animator a in m.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(a); // no controller yet

        // Face +Z using the hand bones (right hand is on +X when facing +Z)
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
        if (b.size.y < 0.05f) { Object.Destroy(m); return false; }
        float s = 1.8f / b.size.y;
        float footOffset = b.min.y - m.transform.position.y;
        Vector3 baseScale = m.transform.localScale;

        m.name = "CharacterModel";
        m.transform.SetParent(player.transform, false);
        m.transform.localRotation = faceFix;
        m.transform.localScale = baseScale * s;
        m.transform.localPosition = new Vector3(0f, -footOffset * s, 0f);
        return true;
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
    }

    private static Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // ------------------------------------------------------------------ Helpers
    private static Material Mat(Color c)
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

    private static Material Unlit(Color c)
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) return Mat(c);
        Material m = new Material(s);
        m.color = c;
        return m;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    /// <summary>Sets a [SerializeField] private field by name (our scripts keep their fields private).</summary>
    private static void Set(object target, string field, object value)
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
