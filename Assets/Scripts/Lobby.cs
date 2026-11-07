using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FB Coins: the game currency. Saved on the phone with PlayerPrefs.
/// Earn: kill enemies. Spend: upgrades in the lobby shop.
/// </summary>
public static class FBCoins
{
    public const string CurrencyName = "FB COINS";
    public const int MaxLevel = 5;
    public const int StartingCoins = 100;

    private const string KeyCoins = "fb_coins";
    private const string KeyAmmo = "up_ammo";
    private const string KeyArmor = "up_armor";

    public static event System.Action Changed;
    public static event System.Action<int> Gained;

    public static int Balance
    {
        get
        {
            if (!PlayerPrefs.HasKey(KeyCoins))
            {
                PlayerPrefs.SetInt(KeyCoins, StartingCoins);
                PlayerPrefs.Save();
            }
            return PlayerPrefs.GetInt(KeyCoins, StartingCoins);
        }
    }

    public static int AmmoLevel { get { return PlayerPrefs.GetInt(KeyAmmo, 0); } }
    public static int ArmorLevel { get { return PlayerPrefs.GetInt(KeyArmor, 0); } }
    public static int AmmoCost { get { return 100 * (AmmoLevel + 1); } }
    public static int ArmorCost { get { return 150 * (ArmorLevel + 1); } }

    public static void Add(int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(KeyCoins, Balance + amount);
        PlayerPrefs.Save();
        if (Changed != null) Changed();
        if (Gained != null) Gained(amount);
    }

    public static bool TrySpend(int cost)
    {
        int b = Balance;
        if (b < cost) return false;
        PlayerPrefs.SetInt(KeyCoins, b - cost);
        PlayerPrefs.Save();
        if (Changed != null) Changed();
        return true;
    }

    public static bool BuyAmmo()
    {
        if (AmmoLevel >= MaxLevel) return false;
        if (!TrySpend(AmmoCost)) return false;
        PlayerPrefs.SetInt(KeyAmmo, AmmoLevel + 1);
        PlayerPrefs.Save();
        if (Changed != null) Changed();
        return true;
    }

    public static bool BuyArmor()
    {
        if (ArmorLevel >= MaxLevel) return false;
        if (!TrySpend(ArmorCost)) return false;
        PlayerPrefs.SetInt(KeyArmor, ArmorLevel + 1);
        PlayerPrefs.Save();
        if (Changed != null) Changed();
        return true;
    }
}

/// <summary>Text that always shows the FB Coins balance (and a short "+N" when coins are earned).</summary>
[RequireComponent(typeof(Text))]
public class CoinLabel : MonoBehaviour
{
    private Text label;
    private float showGainUntil;
    private int lastGain;

    private void Awake() { label = GetComponent<Text>(); }

    private void OnEnable()
    {
        FBCoins.Changed += Refresh;
        FBCoins.Gained += OnGained;
        Refresh();
    }

    private void OnDisable()
    {
        FBCoins.Changed -= Refresh;
        FBCoins.Gained -= OnGained;
    }

    private void OnGained(int n)
    {
        lastGain = n;
        showGainUntil = Time.unscaledTime + 1.5f;
        Refresh();
    }

    private void Refresh()
    {
        if (label == null) return;
        string gain = Time.unscaledTime < showGainUntil ? "   +" + lastGain : "";
        label.text = FBCoins.CurrencyName + ": " + FBCoins.Balance + gain;
    }

    private void Update()
    {
        if (showGainUntil > 0f && Time.unscaledTime >= showGainUntil)
        {
            showGainUntil = 0f;
            Refresh();
        }
    }
}

public class LobbySpinner : MonoBehaviour
{
    private void Update() { transform.Rotate(0f, 25f * Time.deltaTime, 0f); }
}

public class LobbyTicker : MonoBehaviour
{
    public Text toast;
    public float until;

    private void Update()
    {
        if (toast != null && toast.enabled && Time.unscaledTime > until) toast.enabled = false;
    }
}

/// <summary>
/// Main menu: 3D character preview on the left, PLAY / SHOP on the right, FB Coins on top.
/// Built from code; GameBootstrap calls Lobby.Show(startGame).
/// </summary>
public static class Lobby
{
    public const string GameTitle = "CITY WARS 3D";   // change the game name here

    private static GameObject canvasGO, stageGO, camGO, shopPanel;
    private static System.Action onPlay;
    private static Text ammoLabel, ammoBtnText, armorLabel, armorBtnText, toastText;
    private static LobbyTicker ticker;

    public static void Show(System.Action play)
    {
        onPlay = play;
        GameBootstrap.EnsureEventSystem();
        BuildStage();
        BuildUI();
    }

    // ------------------------------------------------------------------ 3D preview
    private static void BuildStage()
    {
        GameObject old = GameObject.Find("Main Camera");
        if (old != null) Object.Destroy(old);
        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject l = new GameObject("Sun");
            l.AddComponent<Light>().type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Vector3 origin = new Vector3(0f, 500f, 0f);
        stageGO = new GameObject("LobbyStage");
        stageGO.transform.position = origin;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "Floor";
        Object.Destroy(floor.GetComponent<Collider>());
        floor.transform.SetParent(stageGO.transform, false);
        floor.transform.localScale = new Vector3(3f, 0.04f, 3f);
        floor.transform.localPosition = new Vector3(0f, -0.04f, 0f);
        floor.GetComponent<Renderer>().sharedMaterial = GameBootstrap.Mat(new Color(0.12f, 0.14f, 0.2f));

        GameObject prefab = Resources.Load<GameObject>("Characters/Ch45_nonPBR");
        if (prefab != null)
        {
            GameObject holder = new GameObject("LobbyChar");
            holder.transform.SetParent(stageGO.transform, false);
            GameObject model = GameBootstrap.AttachModel(prefab, holder.transform, 1.8f, "Characters/Ch45_nonPBR_Diffuse");
            if (model != null)
            {
                ProceduralCharacterAnimator anim = model.AddComponent<ProceduralCharacterAnimator>();
                anim.Init(holder.transform, null, null, null, null);   // idle pose, arms down
                holder.AddComponent<LobbySpinner>();
            }
        }

        camGO = new GameObject("LobbyCamera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.depth = 10f;
        cam.farClipPlane = 900f;
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = origin + new Vector3(1.0f, 1.3f, -3.6f);
        camGO.transform.LookAt(origin + new Vector3(1.0f, 1.0f, 0f));   // character ends up on the left side of the screen
    }

    // ------------------------------------------------------------------ UI
    private static void BuildUI()
    {
        Font font = GameBootstrap.GetFont();

        canvasGO = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasGO.transform;
        ticker = canvasGO.AddComponent<LobbyTicker>();

        // Title (top-left)
        Text title = MakeText("Title", root, GameTitle, 96, TextAnchor.UpperLeft, Color.white, font);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -40f), new Vector2(1100f, 140f));
        title.fontStyle = FontStyle.Bold;

        // FB Coins (top-right)
        RectTransform pill = GameBootstrap.UIRect("CoinPill", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(640f, 90f));
        GameBootstrap.UIImage(pill, null, new Color(0f, 0f, 0f, 0.55f), false);
        Text coins = MakeText("Coins", pill, "", 46, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f), font);
        coins.gameObject.AddComponent<CoinLabel>();

        // PLAY / SHOP (right side)
        Button play = RectButton(root, "PLAY", new Vector2(0.5f, 0.5f), new Vector2(560f, 80f), new Vector2(640f, 180f), new Color(0.15f, 0.65f, 0.25f, 0.95f), 80, font);
        play.onClick.AddListener(OnPlayPressed);
        Button shop = RectButton(root, "SHOP", new Vector2(0.5f, 0.5f), new Vector2(560f, -150f), new Vector2(640f, 130f), new Color(0.2f, 0.45f, 0.85f, 0.95f), 60, font);
        shop.onClick.AddListener(() => { shopPanel.SetActive(true); RefreshShop(); });

        Text tip = MakeText("Tip", root, "Defeat enemies to earn " + FBCoins.CurrencyName, 40, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f), font);
        Place(tip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1400f, 70f));

        BuildShop(root, font);
    }

    private static void BuildShop(Transform root, Font font)
    {
        RectTransform overlay = GameBootstrap.UIRect("ShopPanel", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        GameBootstrap.UIImage(overlay, null, new Color(0f, 0f, 0f, 0.75f), true);
        shopPanel = overlay.gameObject;

        RectTransform box = GameBootstrap.UIRect("Box", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 760f));
        GameBootstrap.UIImage(box, null, new Color(0.09f, 0.11f, 0.16f, 0.98f), true);

        Text head = MakeText("ShopTitle", box, "SHOP", 80, TextAnchor.MiddleCenter, Color.white, font);
        Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(600f, 110f));

        // Rows
        ammoLabel = MakeText("AmmoLabel", box, "", 40, TextAnchor.MiddleLeft, Color.white, font);
        Place(ammoLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180f, 120f), new Vector2(760f, 160f));
        Button ammoBtn = RectButton(box, "BUY", new Vector2(0.5f, 0.5f), new Vector2(440f, 120f), new Vector2(300f, 110f), new Color(0.15f, 0.6f, 0.25f), 44, font);
        ammoBtnText = ammoBtn.GetComponentInChildren<Text>();
        ammoBtn.onClick.AddListener(() =>
        {
            if (!FBCoins.BuyAmmo()) Toast(FBCoins.AmmoLevel >= FBCoins.MaxLevel ? "Already at max level" : "Not enough " + FBCoins.CurrencyName);
            RefreshShop();
        });

        armorLabel = MakeText("ArmorLabel", box, "", 40, TextAnchor.MiddleLeft, Color.white, font);
        Place(armorLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180f, -70f), new Vector2(760f, 160f));
        Button armorBtn = RectButton(box, "BUY", new Vector2(0.5f, 0.5f), new Vector2(440f, -70f), new Vector2(300f, 110f), new Color(0.15f, 0.6f, 0.25f), 44, font);
        armorBtnText = armorBtn.GetComponentInChildren<Text>();
        armorBtn.onClick.AddListener(() =>
        {
            if (!FBCoins.BuyArmor()) Toast(FBCoins.ArmorLevel >= FBCoins.MaxLevel ? "Already at max level" : "Not enough " + FBCoins.CurrencyName);
            RefreshShop();
        });

        toastText = MakeText("Toast", box, "", 42, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f), font);
        Place(toastText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(1000f, 70f));
        toastText.enabled = false;
        ticker.toast = toastText;

        Button close = RectButton(box, "CLOSE", new Vector2(0.5f, 0.5f), new Vector2(0f, -310f), new Vector2(360f, 100f), new Color(0.75f, 0.2f, 0.2f), 46, font);
        close.onClick.AddListener(() => shopPanel.SetActive(false));

        shopPanel.SetActive(false);
    }

    private static void RefreshShop()
    {
        int a = FBCoins.AmmoLevel;
        ammoLabel.text = "AMMO PACK  (+60 reserve ammo per level)\nLevel " + a + " / " + FBCoins.MaxLevel;
        ammoBtnText.text = a >= FBCoins.MaxLevel ? "MAX" : "BUY  " + FBCoins.AmmoCost;

        int r = FBCoins.ArmorLevel;
        armorLabel.text = "ARMOR  (+25 max health per level)\nLevel " + r + " / " + FBCoins.MaxLevel;
        armorBtnText.text = r >= FBCoins.MaxLevel ? "MAX" : "BUY  " + FBCoins.ArmorCost;
    }

    private static void Toast(string message)
    {
        toastText.text = message;
        toastText.enabled = true;
        ticker.until = Time.unscaledTime + 1.6f;
    }

    private static void OnPlayPressed()
    {
        Object.Destroy(canvasGO);
        Object.Destroy(stageGO);
        Object.Destroy(camGO);
        System.Action start = onPlay;
        onPlay = null;
        if (start != null) start();
    }

    // ------------------------------------------------------------------ UI helpers (also used by the HUD)
    internal static Button RectButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color color, int fontSize, Font font)
    {
        RectTransform rt = GameBootstrap.UIRect(label + "Button", parent, anchor, anchor, new Vector2(0.5f, 0.5f), pos, size);
        Image img = GameBootstrap.UIImage(rt, null, color, true);
        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        Text t = MakeText("Label", rt, label, fontSize, TextAnchor.MiddleCenter, Color.white, font);
        t.fontStyle = FontStyle.Bold;
        return b;
    }

    private static Text MakeText(string name, Transform parent, string text, int size, TextAnchor align, Color color, Font font)
    {
        RectTransform rt = GameBootstrap.UIRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
