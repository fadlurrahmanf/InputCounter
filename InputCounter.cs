using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class InputCounterForm : Form
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string moduleName);

    private const int FastThresholdKeysPerSecond = 4;
    // The HUD itself is intentionally narrow.  The independent Aura window
    // keeps the visual companion spacious without making the counter card wide.
    private const int HudWidth = 150;
    private const int HudHeight = 120;
    // Keep the transparent Aura surface just larger than its farthest incoming
    // particle: large enough to avoid clipping, but no longer screen-sized.
    private const int AuraWindowSize = 720;
    private const int AuraReferenceWindowSize = 75;
    private const float AuraCoreCoordinate = AuraWindowSize / 2F;
    private static readonly Color ScoreBonusAuraColor = Color.FromArgb(255, 72, 60);
    private static readonly Color RewindAuraColor = Color.FromArgb(72, 255, 142);
    // Normal builds never inspect key sequences. The rewind keyword trigger was
    // only enabled in the separate temporary demo executable.
    private static readonly bool DebugRewindKeywordTrigger = false;
    private const int MaxRenderedAuraParticles = 1000;
    private readonly Label totalLabel = new Label(), totalHighScoreLabel = new Label(), detailLabel = new Label(), highScoreLabel = new Label(), allTimeLabel = new Label();
    private readonly Panel hudBackgroundPanel = new Panel();
    private readonly NotifyIcon trayIcon = new NotifyIcon();
    private HookProc keyboardProc, mouseProc;
    private IntPtr keyboardHook, mouseHook;
    private long totalCount;
    private DateTime totalDate = DateTime.Today;
    private readonly HashSet<int> heldKeys = new HashSet<int>();
    private readonly Queue<int> debugRewindKeys = new Queue<int>();
    private DateTime lastInputAt = DateTime.UtcNow;
    private readonly Queue<DateTime> recentInputs = new Queue<DateTime>();
    private readonly Timer rateTimer = new Timer();
    private readonly Timer shakeTimer = new Timer();
    private readonly Timer totalAnimationTimer = new Timer();
    private readonly Timer auraTimer = new Timer();
    private readonly Timer bubbleTimer = new Timer();
    private readonly Random random = new Random();
    private int hudTop;
    private AuraWindow auraWindow;
    private Point restingLocation;
    private bool applyingShake;
    private int shakeXAmplitude = 1;
    private int shakeYAmplitude = 1;
    private Point totalBaseLocation;
    private Color currentSpeedColor = Color.White;
    private int increaseAnimationFrames;
    private int decreaseAnimationFrames;
    private readonly List<Particle> particles = new List<Particle>();
    private readonly List<FloatingEffect> floatingEffects = new List<FloatingEffect>();
    private readonly List<AuraParticle> auraParticles = new List<AuraParticle>();
    private readonly HashSet<string> dailyPlanets = new HashSet<string>();
    private readonly Queue<string> queuedPlanetGacha = new Queue<string>();
    private readonly Queue<RareBubbleMessage> queuedRareBubbles = new Queue<RareBubbleMessage>();
    private readonly string highScorePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "highscore-1-sec.txt");
    private readonly string totalPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "current-total.txt");
    private readonly string totalHighScorePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "total-highscore.txt");
    private readonly string allTimePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "all-time-total-keys.txt");
    private readonly string auraDiaryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "typing-aura-diary.txt");
    private readonly string planetCollectionPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "planet-collection.txt");
    private readonly string planetHistoryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "planet-history.txt");
    private int highScore;
    private long totalHighScore;
    private long allTimeTotal;
    private bool allTimeDirty;
    private bool totalDirty;
    private bool totalHighScoreDirty;
    private long lastInputCheckpoint;
    private bool rewindGreenActive;
    private bool rewindCooldownActive;
    private double rewindDecayDebt;
    private readonly List<string> pendingLogs = new List<string>();
    private AuraDiary auraDiary;
    private bool auraDiaryDirty;
    private double auraSpeed;
    private double auraMass;
    private double auraPhase;
    private ElementStyle activeElement = ElementStyle.Light;
    private int sessionPeakRate;
    private long sessionInputCount;
    private int sessionCriticalCount;
    private static readonly string[] InputEventNames = { "CRIT +2", "CRIT +3", "CRIT +4", "CRIT +5" };
    private static readonly string[] IdleEventNames = { "REWIND", "ECHO +1", "ECHO +3", "ECHO +10", "ECHO +20", "DECAY -2", "DECAY -3", "DECAY -5" };
    private readonly Dictionary<string, long> inputEventKeyPressCounts = new Dictionary<string, long>();
    private readonly Dictionary<string, long> idleEventDecayedKeyCounts = new Dictionary<string, long>();
    private string bubbleText = "Core ready";
    private DateTime bubbleAppearedAt = DateTime.UtcNow;
    private DateTime bubbleRareUntil = DateTime.MinValue;
    private DateTime nextBubbleUpdateAt = DateTime.UtcNow;
    private bool bubbleShowsRareChance;
    private Color bubbleFlashColorA = Color.White;
    private Color bubbleFlashColorB = Color.FromArgb(120, 195, 255);
    private DateTime planetCollectionDate = DateTime.Today;
    private DateTime auraDecayRedUntil = DateTime.MinValue;
    private DateTime auraRewindGreenUntil = DateTime.MinValue;
    private bool planetGachaActive;
    private DateTime planetGachaStartedAt;
    private string planetGachaResult = String.Empty;
    private static readonly string[] PlanetNames = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };

    public InputCounterForm()
    {
        Text = "Input Counter"; TopMost = true; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(156, 180);
        // A neutral transparency key prevents semi-transparent glow pixels from
        // inheriting the former fuchsia key colour and appearing pink.
        BackColor = Color.Black; TransparencyKey = Color.Black; Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        // Keep the running app discoverable in Windows' notification area
        // without reserving a taskbar slot for the always-visible HUD.
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "Input Counter";
        trayIcon.Visible = true;
        Color hudBackground = Color.FromArgb(29, 31, 38);
        hudTop = ClientSize.Height - HudHeight;
        hudBackgroundPanel.BackColor = hudBackground; hudBackgroundPanel.Location = new Point(0, hudTop); hudBackgroundPanel.Size = new Size(HudWidth, HudHeight);
        totalLabel.BackColor = hudBackground; totalLabel.ForeColor = Color.White; totalLabel.Font = new Font("Segoe UI Semibold", 19F); totalLabel.Location = new Point(4, hudTop + 2); totalLabel.Size = new Size(HudWidth - 8, 31); totalLabel.TextAlign = ContentAlignment.MiddleCenter;
        totalHighScoreLabel.BackColor = hudBackground; totalHighScoreLabel.ForeColor = Color.FromArgb(205, 210, 220); totalHighScoreLabel.Font = new Font("Segoe UI", 6.5F); totalHighScoreLabel.Location = new Point(4, hudTop + 35); totalHighScoreLabel.Size = new Size(HudWidth - 8, 11); totalHighScoreLabel.TextAlign = ContentAlignment.MiddleCenter;
        detailLabel.BackColor = hudBackground; detailLabel.ForeColor = Color.FromArgb(163, 196, 255); detailLabel.Font = new Font("Segoe UI", 8F); detailLabel.Location = new Point(4, hudTop + 48); detailLabel.Size = new Size(HudWidth - 8, 15); detailLabel.TextAlign = ContentAlignment.MiddleCenter;
        highScoreLabel.BackColor = hudBackground; highScoreLabel.ForeColor = Color.FromArgb(163, 196, 255); highScoreLabel.Font = new Font("Segoe UI", 6.5F); highScoreLabel.Location = new Point(4, hudTop + 66); highScoreLabel.Size = new Size(HudWidth - 8, 13); highScoreLabel.TextAlign = ContentAlignment.MiddleCenter;
        allTimeLabel.BackColor = hudBackground; allTimeLabel.ForeColor = Color.FromArgb(205, 210, 220); allTimeLabel.Font = new Font("Segoe UI", 6.5F); allTimeLabel.Location = new Point(4, hudTop + 83); allTimeLabel.Size = new Size(HudWidth - 8, 13); allTimeLabel.TextAlign = ContentAlignment.MiddleCenter;
        highScore = 0;
        totalCount = LoadTotalCount();
        lastInputCheckpoint = totalCount;
        totalHighScore = LoadTotalHighScore();
        allTimeTotal = LoadAllTimeTotal();
        auraDiary = LoadAuraDiary();
        LoadPlanetCollection();
        activeElement = ResolveAutoElement();
        foreach (string eventName in InputEventNames) inputEventKeyPressCounts[eventName] = 0;
        foreach (string eventName in IdleEventNames) idleEventDecayedKeyCounts[eventName] = 0;
        Controls.Add(hudBackgroundPanel); Controls.Add(totalLabel); Controls.Add(totalHighScoreLabel); Controls.Add(detailLabel); Controls.Add(highScoreLabel); Controls.Add(allTimeLabel); hudBackgroundPanel.SendToBack(); UpdateDisplay();
        SyncAuraParticlesToScore();
        MouseDown += WindowMouseDown; hudBackgroundPanel.MouseDown += WindowMouseDown; totalLabel.MouseDown += WindowMouseDown; totalHighScoreLabel.MouseDown += WindowMouseDown; detailLabel.MouseDown += WindowMouseDown; highScoreLabel.MouseDown += WindowMouseDown; allTimeLabel.MouseDown += WindowMouseDown;
        totalBaseLocation = totalLabel.Location;
        rateTimer.Interval = 1000;
        rateTimer.Tick += delegate
        {
            EnsurePlanetCollectionDate();
            if (!EnsureDailyTotal() && DateTime.UtcNow - lastInputAt >= TimeSpan.FromSeconds(3)) RunIdleChance();
            if (allTimeDirty) SaveAllTimeTotal();
            if (totalDirty) SaveTotalCount();
            if (totalHighScoreDirty) SaveTotalHighScore();
            if (auraDiaryDirty) SaveAuraDiary();
            FlushLogs();
            UpdateDisplay();
        };
        rateTimer.Start();
        totalAnimationTimer.Interval = 35;
        totalAnimationTimer.Tick += delegate { RenderTotalAnimation(); };
        auraTimer.Interval = 35;
        auraTimer.Tick += delegate { UpdateTypingAura(); };
        auraTimer.Start();
        bubbleTimer.Interval = 100;
        bubbleTimer.Tick += delegate { UpdateBubble(); };
        bubbleTimer.Start();
        Paint += delegate(object sender, PaintEventArgs e) { DrawParticles(e.Graphics, false); DrawBubble(e.Graphics); DrawParticles(e.Graphics, true); DrawFloatingEffects(e.Graphics); };
        restingLocation = Location;
        LocationChanged += delegate { if (!applyingShake) restingLocation = Location; };
        Shown += delegate
        {
            MoveToBottomLeft();
            auraWindow = new AuraWindow(this);
            PointF core = AuraCoreCenter();
            auraWindow.Location = new Point(Location.X + HudWidth / 2 - (int)core.X, Location.Y - 108 - (int)core.Y);
            auraWindow.Show();
        };
        shakeTimer.Interval = 55;
        shakeTimer.Tick += delegate
        {
            applyingShake = true;
            Location = new Point(restingLocation.X + random.Next(-shakeXAmplitude, shakeXAmplitude + 1), restingLocation.Y + random.Next(-shakeYAmplitude, shakeYAmplitude + 1));
            applyingShake = false;
        };
        keyboardProc = KeyboardCallback; mouseProc = MouseCallback;
        keyboardHook = InstallHook(WH_KEYBOARD_LL, keyboardProc); mouseHook = InstallHook(WH_MOUSE_LL, mouseProc);
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero) MessageBox.Show("Global input hook tidak dapat dipasang. Jalankan ulang aplikasi.", "Input Counter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private static IntPtr InstallHook(int type, HookProc proc)
    { using (Process p = Process.GetCurrentProcess()) using (ProcessModule m = p.MainModule) return SetWindowsHookEx(type, proc, GetModuleHandle(m.ModuleName), 0); }
    private ElementStyle ResolveAutoElement()
    {
        // The style is selected only at session start.  It uses aggregate rhythm
        // from the previous session, never a key name, character, or typed text.
        if (auraDiary.SessionCount == 0) { auraDiary.SessionCount = 1; auraDiary.LastAutoElement = (int)ElementStyle.Light; auraDiaryDirty = true; return ElementStyle.Light; }
        double[] weights = { 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.85, 0.45, 0.85 };
        double fast = 1.0 - Math.Exp(-Math.Max(0, auraDiary.LastSessionPeak) / 12.0);
        double steady = (1.0 - Math.Exp(-Math.Max(0, auraDiary.LastSessionInputs) / 480.0)) * (1.0 - fast * 0.45);
        double critical = 1.0 - Math.Exp(-Math.Max(0, auraDiary.LastSessionCriticals) / 8.0);
        double lowTotal = auraDiary.LastSessionTotal <= 0 ? 1.0 : Math.Exp(-Math.Max(0, auraDiary.LastSessionTotal) / 90.0);
        double memory = 1.0 - Math.Exp(-Math.Log(1.0 + Math.Max(0, allTimeTotal)) / 6.0);
        weights[(int)ElementStyle.Wind - 1] += fast * 1.8;
        weights[(int)ElementStyle.Lightning - 1] += fast * 1.5;
        weights[(int)ElementStyle.Water - 1] += steady * 1.45;
        weights[(int)ElementStyle.Nature - 1] += steady * 1.25;
        weights[(int)ElementStyle.Light - 1] += critical * 1.5;
        weights[(int)ElementStyle.Cosmic - 1] += critical * 1.3 + memory * 0.65;
        weights[(int)ElementStyle.Shadow - 1] += lowTotal * 1.2;
        weights[(int)ElementStyle.Ice - 1] += lowTotal * 0.9;
        if (auraDiary.LastAutoElement >= (int)ElementStyle.Fire && auraDiary.LastAutoElement <= (int)ElementStyle.Cosmic)
            weights[auraDiary.LastAutoElement - 1] += 1.5; // gentle continuity between sessions
        double totalWeight = 0; for (int i = 0; i < weights.Length; i++) totalWeight += weights[i];
        int seed = Math.Abs(auraDiary.SessionCount * 7919 + auraDiary.LastSessionPeak * 97 + auraDiary.LastSessionCriticals * 31 + (int)(auraDiary.LastSessionInputs % 997));
        double pick = (seed % 10000) / 10000.0 * totalWeight;
        int chosen = 0; while (chosen < weights.Length - 1 && (pick -= weights[chosen]) > 0) chosen++;
        ElementStyle resolved = (ElementStyle)(chosen + 1);
        auraDiary.LastAutoElement = (int)resolved;
        auraDiary.SessionCount++;
        auraDiaryDirty = true;
        return resolved;
    }
    private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            int virtualKey = Marshal.ReadInt32(lParam);
            if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && heldKeys.Add(virtualKey)) { RecordPhysicalKeyPress(); RegisterInput(); CheckDebugRewindKeyword(virtualKey); }
            else if (message == WM_KEYUP || message == WM_SYSKEYUP) heldKeys.Remove(virtualKey);
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }
    private void RecordPhysicalKeyPress()
    {
        foreach (string eventName in InputEventNames) inputEventKeyPressCounts[eventName]++;
    }
    private void RecordDecayedKeys(long amount)
    {
        if (amount <= 0) return;
        foreach (string eventName in IdleEventNames) idleEventDecayedKeyCounts[eventName] += amount;
    }
    private void CheckDebugRewindKeyword(int virtualKey)
    {
        if (!DebugRewindKeywordTrigger) return;
        int[] word = { 0x52, 0x45, 0x57, 0x49, 0x4E, 0x44 }; // R E W I N D
        debugRewindKeys.Enqueue(virtualKey);
        while (debugRewindKeys.Count > word.Length) debugRewindKeys.Dequeue();
        if (debugRewindKeys.Count != word.Length) return;
        int position = 0;
        foreach (int key in debugRewindKeys) { if (key != word[position++]) return; }
        debugRewindKeys.Clear();
        TriggerDebugRewind();
    }
    private void TriggerDebugRewind()
    {
        long before = totalCount;
        totalCount = lastInputCheckpoint;
        SyncAuraParticlesToScore();
        rewindGreenActive = true;
        rewindCooldownActive = true;
        rewindDecayDebt = 0;
        SpawnRewindEffect();
        totalAnimationTimer.Start();
        LogChange("DEBUG_REWIND", totalCount - before, before, totalCount, recentInputs.Count, 0, lastInputCheckpoint);
        UpdateDisplay();
    }
    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    { if (code >= 0) { int m = wParam.ToInt32(); if (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN || m == WM_MBUTTONDOWN || m == WM_XBUTTONDOWN) RegisterInput(); } return CallNextHookEx(mouseHook, code, wParam, lParam); }
    private void RegisterInput()
    {
        EnsureDailyTotal();
        EnsurePlanetCollectionDate();
        TryPlanetGacha();
        DateTime now = DateTime.UtcNow;
        DateTime cutoff = now.AddSeconds(-1);
        while (recentInputs.Count > 0 && recentInputs.Peek() < cutoff) recentInputs.Dequeue();
        recentInputs.Enqueue(now);
        int multiplier = DetermineMultiplier(recentInputs.Count);
        totalCount += multiplier;
        totalDirty = true;
        allTimeTotal += multiplier;
        allTimeDirty = true;
        sessionInputCount++;
        if (recentInputs.Count > sessionPeakRate) sessionPeakRate = recentInputs.Count;
        if (multiplier > 1) sessionCriticalCount++;
        if (recentInputs.Count > auraDiary.BestBurst) { auraDiary.BestBurst = recentInputs.Count; auraDiaryDirty = true; }
        if (recentInputs.Count > auraDiary.PeakKeysPerSecond) { auraDiary.PeakKeysPerSecond = recentInputs.Count; auraDiaryDirty = true; }
        if (auraDiary.ActiveDays.Add(DateTime.Now.ToString("yyyy-MM-dd"))) auraDiaryDirty = true;
        rewindGreenActive = false;
        rewindCooldownActive = false;
        rewindDecayDebt = 0;
        lastInputCheckpoint = totalCount;
        lastInputAt = now;
        int auraCountBeforeInput = auraParticles.Count;
        Color? auraArrivalColor = multiplier > 1 ? (Color?)ScoreBonusAuraColor : null;
        SyncAuraParticlesToScore(auraArrivalColor);
        RefreshIncomingAuraParticles(auraCountBeforeInput, multiplier, auraArrivalColor);
        StartIncreaseAnimation(multiplier);
        LogChange(multiplier > 1 ? "INPUT_CRITICAL" : "INPUT_NORMAL", multiplier, totalCount - multiplier, totalCount, recentInputs.Count, multiplier, null);
        UpdateDisplay();
    }
    private void EnsurePlanetCollectionDate()
    {
        DateTime today = DateTime.Today;
        if (planetCollectionDate == today) return;
        planetCollectionDate = today;
        dailyPlanets.Clear();
        SavePlanetCollection();
    }
    private void LoadPlanetCollection()
    {
        try
        {
            string[] data = System.IO.File.ReadAllText(planetCollectionPath).Split('|');
            DateTime savedDate;
            if (data.Length == 2 && DateTime.TryParseExact(data[0], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out savedDate))
            {
                planetCollectionDate = savedDate.Date;
                if (planetCollectionDate == DateTime.Today)
                    foreach (string planet in data[1].Split(',')) if (IsKnownPlanet(planet)) dailyPlanets.Add(planet);
            }
        }
        catch { }
        EnsurePlanetCollectionDate();
    }
    private bool IsKnownPlanet(string planet)
    {
        foreach (string name in PlanetNames) if (name == planet) return true;
        return false;
    }
    private void SavePlanetCollection()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(planetCollectionPath));
            System.IO.File.WriteAllText(planetCollectionPath, planetCollectionDate.ToString("yyyy-MM-dd") + "|" + string.Join(",", dailyPlanets));
        }
        catch { }
    }
    private void RecordPlanetHistory(string planet)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(planetHistoryPath));
            System.IO.File.AppendAllText(planetHistoryPath, DateTime.Today.ToString("yyyy-MM-dd") + "|" + planet + Environment.NewLine);
        }
        catch { }
    }
    private void TryPlanetGacha()
    {
        if (dailyPlanets.Count >= PlanetNames.Length || random.NextDouble() >= 0.00001) return;
        List<string> available = new List<string>();
        foreach (string planet in PlanetNames) if (!dailyPlanets.Contains(planet)) available.Add(planet);
        if (available.Count == 0) return;
        string won = available[random.Next(available.Count)];
        dailyPlanets.Add(won);
        SavePlanetCollection();
        RecordPlanetHistory(won);
        if (planetGachaActive) queuedPlanetGacha.Enqueue(won); else StartPlanetGacha(won);
    }
    private void StartPlanetGacha(string won)
    {
        planetGachaResult = won;
        planetGachaStartedAt = DateTime.UtcNow;
        planetGachaActive = true;
    }
    private int DetermineMultiplier(int physicalKeysPerSecond)
    {
        if (physicalKeysPerSecond <= 10) return 1;
        double roll = random.NextDouble() * 100.0;
        if (roll < 85.0) return 1;
        if (roll < 95.0) { ShowRareEventBubble("CRIT +2", 10.0, true); return 2; }
        if (roll < 98.0) { ShowRareEventBubble("CRIT +3", 3.0, true); return 3; }
        if (roll < 99.5) { ShowRareEventBubble("CRIT +4", 1.5, true); return 4; }
        ShowRareEventBubble("CRIT +5", 0.5, true); return 5;
    }
    private void RunIdleChance()
    {
        long before = totalCount;
        // Rewind is a separate 0.001% idle roll so all existing decay/Echo
        // probabilities retain their own distribution.
        if (!rewindCooldownActive && random.NextDouble() < 0.00001)
        {
            RecordDecayedKeys(Math.Max(0, before - lastInputCheckpoint));
            long doubled = before > long.MaxValue / 2 ? long.MaxValue : before < long.MinValue / 2 ? long.MinValue : before * 2;
            // A Rewind may recover up to the user's recorded High Total, never
            // leap over it. This intentionally does not change All Time.
            totalCount = totalHighScore > 0 && doubled > totalHighScore ? totalHighScore : doubled;
            totalDirty = true;
            int auraCountBeforeRewind = auraParticles.Count;
            int restored = (int)Math.Min(MaxRenderedAuraParticles, Math.Max(0L, totalCount - before));
            SyncAuraParticlesToScore();
            RefreshIncomingAuraParticles(auraCountBeforeRewind, restored, null);
            ActivateRewindAuraGreenEffect();
            rewindGreenActive = true;
            rewindCooldownActive = true;
            rewindDecayDebt = 0;
            ShowRareEventBubble("REWIND", 0.001, false);
            SpawnRewindEffect();
            LogChange("REWIND", totalCount - before, before, totalCount, recentInputs.Count, 0, totalHighScore);
            UpdateDisplay();
            return;
        }
        double roll = random.NextDouble() * 100.0;
        int delta;
        string eventName;
        double chancePercent;
        if (roll < 95.0) { delta = -1; chancePercent = 95.0; }
        else if (roll < 97.5) { delta = -2; chancePercent = 2.5; }
        else if (roll < 98.5) { delta = -3; chancePercent = 1.0; }
        else if (roll < 98.9) { delta = -5; chancePercent = 0.4; }
        else if (roll < 99.4) { delta = 1; chancePercent = 0.5; }
        else if (roll < 99.6) { delta = 3; chancePercent = 0.2; }
        else if (roll < 99.7) { delta = 10; chancePercent = 0.1; }
        else { delta = 20; chancePercent = 0.03; }
        // A normal -1 remains reliable once per idle second. Only rare Decay
        // and Echo candidates can fall back to that normal decay.
        if (delta > 0 && random.NextDouble() >= 0.5) { delta = -1; chancePercent = 100.0; }
        else if (delta < -1 && random.NextDouble() >= 0.25) { delta = -1; chancePercent = 100.0; }
        if (delta == -1) chancePercent = 100.0;
        else if (delta == -2) chancePercent = 0.625;
        else if (delta == -3) chancePercent = 0.25;
        else if (delta == -5) chancePercent = 0.1;
        else if (delta == 1) chancePercent = 0.25;
        else if (delta == 3) chancePercent = 0.1;
        else if (delta == 10) chancePercent = 0.05;
        else if (delta == 20) chancePercent = 0.015;
        if (delta < 0) RecordDecayedKeys(-delta);
        // Normal -1 decay stays deliberately quiet; only special decay values
        // and every Echo use the event bubble.
        if ((delta < -1) || delta > 0)
        {
            if (delta < 0) ShowRareEventBubble("DECAY " + delta, chancePercent, false);
            else ShowRareEventBubble("ECHO +" + delta, chancePercent, false);
        }
        totalCount += delta;
        totalDirty = true;
        if (delta < -1) ActivateDecayAuraRedEffect();
        int auraCountBeforeIdleEvent = auraParticles.Count;
        Color? echoArrivalColor = delta > 0 ? (Color?)ScoreBonusAuraColor : null;
        SyncAuraParticlesToScore(echoArrivalColor);
        if (delta > 0) RefreshIncomingAuraParticles(auraCountBeforeIdleEvent, delta, echoArrivalColor);
        if (delta < 0)
        {
            eventName = delta == -1 ? "DECAY_NORMAL" : "DECAY_CRITICAL";
            if (rewindGreenActive) { rewindDecayDebt += -delta; if (rewindDecayDebt >= 100) { rewindGreenActive = false; rewindCooldownActive = false; } }
            StartDecreaseAnimation(delta == -1, delta < -1);
        }
        else { eventName = "ECHO_CASCADE"; StartEchoCascadeAnimation(delta); }
        LogChange(eventName, delta, before, totalCount, recentInputs.Count, delta, null);
    }
    private void LogChange(string eventName, long delta, long before, long after, int keyPerSecond, int multiplier, long? checkpoint)
    {
        string checkpointField = checkpoint.HasValue ? ",\"checkpoint\":" + checkpoint.Value : "";
        pendingLogs.Add("{\"timestamp\":\"" + DateTime.Now.ToString("o") + "\",\"event\":\"" + eventName + "\",\"delta\":" + delta + ",\"total_before\":" + before + ",\"total_after\":" + after + ",\"key_per_sec\":" + keyPerSecond + ",\"multiplier\":" + multiplier + checkpointField + "}");
    }
    private void FlushLogs()
    {
        if (pendingLogs.Count == 0) return;
        try
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputCounter", "logs");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllLines(System.IO.Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl"), pendingLogs);
            pendingLogs.Clear();
        }
        catch { }
    }
    private void UpdateDisplay()
    {
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-1);
        while (recentInputs.Count > 0 && recentInputs.Peek() < cutoff) recentInputs.Dequeue();
        int keysPerSecond = recentInputs.Count;
        if (keysPerSecond > highScore) highScore = keysPerSecond;
        if (totalCount > totalHighScore) { totalHighScore = totalCount; totalHighScoreDirty = true; }
        totalLabel.Text = totalCount.ToString("N0");
        FitTotalFont();
        totalHighScoreLabel.Text = "High: " + totalHighScore.ToString("N0");
        detailLabel.Text = keysPerSecond.ToString("N0") + " key/sec";
        highScoreLabel.Text = "Highscore: " + highScore.ToString("N0") + " key/sec";
        allTimeLabel.Text = "All time: " + allTimeTotal.ToString("N0") + " key";
        ApplySpeedEffects(keysPerSecond);
    }
    private void ApplySpeedEffects(int keysPerSecond)
    {
        // Exponential easing never reaches a hard colour cap: each extra key/sec keeps moving toward deep blue.
        double intensity = 1.0 - Math.Exp(-keysPerSecond / 7.0);
        Color speedColor = Color.FromArgb(
            (int)(255 - 187 * intensity),
            (int)(255 - 110 * intensity),
            255);
        if (rewindGreenActive)
        {
            double greenIntensity = Math.Max(0.0, 1.0 - rewindDecayDebt / 100.0);
            speedColor = Blend(speedColor, Color.FromArgb(70, 255, 130), greenIntensity);
        }
        currentSpeedColor = speedColor;
        if (!totalAnimationTimer.Enabled) totalLabel.ForeColor = speedColor;
        detailLabel.ForeColor = speedColor;
        if (keysPerSecond > FastThresholdKeysPerSecond)
        {
            double shakeIntensity = 1.0 - Math.Exp(-(keysPerSecond - FastThresholdKeysPerSecond) / 6.0);
            shakeXAmplitude = 1 + (int)Math.Round(shakeIntensity * 6.0);
            shakeYAmplitude = 1 + (int)Math.Round(shakeIntensity * 4.0);
            shakeTimer.Interval = 110 - (int)Math.Round(shakeIntensity * 75.0);
            if (!shakeTimer.Enabled) { restingLocation = Location; shakeTimer.Start(); }
        }
        else
        {
            shakeTimer.Stop();
            applyingShake = true;
            Location = restingLocation;
            applyingShake = false;
        }
    }
    private void StartIncreaseAnimation(int multiplier)
    {
        increaseAnimationFrames = 7;
        decreaseAnimationFrames = 0;
        SpawnParticles(true, false, multiplier > 1 ? multiplier : 4, multiplier > 1);
        if (multiplier > 1) SpawnCriticalEffect(multiplier); else SpawnFloatingEffect(true);
        totalAnimationTimer.Start();
    }
    private void StartDecreaseAnimation(bool showFloatingNumber, bool specialEvent)
    {
        decreaseAnimationFrames = 7;
        increaseAnimationFrames = 0;
        SpawnParticles(false, specialEvent, 3, false);
        if (showFloatingNumber) SpawnFloatingEffect(false);
        totalAnimationTimer.Start();
    }
    private void StartEchoCascadeAnimation(int delta)
    {
        increaseAnimationFrames = 7;
        decreaseAnimationFrames = 0;
        SpawnParticles(true, true, Math.Min(20, Math.Max(1, delta)), false);
        SpawnEchoCascadeEffect(delta);
        totalAnimationTimer.Start();
    }
    private void RenderTotalAnimation()
    {
        UpdateParticles();
        UpdateFloatingEffects();
        Invalidate();
        totalLabel.Location = totalBaseLocation;
        if (increaseAnimationFrames > 0)
        {
            double phase = (7 - increaseAnimationFrames) / 6.0;
            int rise = (int)Math.Round(Math.Sin(phase * Math.PI) * 4.0);
            totalLabel.Location = new Point(totalBaseLocation.X, totalBaseLocation.Y - rise);
            totalLabel.ForeColor = Blend(currentSpeedColor, Color.White, Math.Sin(phase * Math.PI) * 0.75);
            increaseAnimationFrames--;
        }
        else if (decreaseAnimationFrames > 0)
        {
            double phase = (7 - decreaseAnimationFrames) / 6.0;
            int drop = (int)Math.Round(Math.Sin(phase * Math.PI) * 3.0);
            totalLabel.Location = new Point(totalBaseLocation.X, totalBaseLocation.Y + drop);
            totalLabel.ForeColor = Blend(currentSpeedColor, Color.FromArgb(255, 85, 55), Math.Sin(phase * Math.PI) * 0.75);
            decreaseAnimationFrames--;
        }
        if (increaseAnimationFrames <= 0 && decreaseAnimationFrames <= 0 && particles.Count == 0 && floatingEffects.Count == 0)
        {
            totalLabel.Location = totalBaseLocation;
            totalLabel.ForeColor = currentSpeedColor;
            totalAnimationTimer.Stop();
        }
    }
    private void SpawnParticles(bool increasing, bool specialEvent, int count, bool criticalRed)
    {
        for (int i = 0; i < count; i++)
        {
            Particle particle = new Particle();
            particle.X = totalBaseLocation.X + 42 + random.Next(-12, 13);
            particle.Y = totalBaseLocation.Y + 18 + random.Next(-8, 9);
            // Pick one of the six visible flight directions independently for every star.
            // This intentionally avoids the former fixed upper-right bias.
            int direction = random.Next(6);
            float[] directionX = { 1F, 1F, 1F, -1F, -1F, -1F };
            float[] directionY = { -0.85F, 0F, 0.85F, -0.85F, 0F, 0.85F };
            float speed = increasing ? 1.7F + (float)random.NextDouble() * 1.8F : 1.1F + (float)random.NextDouble() * 1.3F;
            particle.VelocityX = directionX[direction] * speed;
            particle.VelocityY = directionY[direction] * speed;
            particle.Life = particle.MaxLife = increasing ? 15 + random.Next(7) : 13 + random.Next(6);
            particle.IsStar = increasing;
            particle.IsSpecial = specialEvent;
            particle.IsCritical = criticalRed;
            particle.Color = criticalRed ? Color.FromArgb(255, 72, 60) : increasing
                ? new[] { Color.FromArgb(90, 185, 255), Color.FromArgb(125, 205, 255), Color.FromArgb(180, 225, 255) }[random.Next(3)]
                : Color.FromArgb(85, 140, 235);
            particles.Add(particle);
        }
    }
    private void SpawnDecayShatter(int strength)
    {
        // Special decays visibly fracture into short-lived red shards. These are
        // cosmetic only: their count and colour never affect the counter.
        int count = 5 + strength * 2;
        for (int i = 0; i < count; i++)
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            float speed = 1.45F + (float)random.NextDouble() * (1.2F + strength * 0.16F);
            Particle shard = new Particle();
            shard.X = totalBaseLocation.X + 44 + random.Next(-14, 15);
            shard.Y = totalBaseLocation.Y + 17 + random.Next(-10, 11);
            shard.VelocityX = (float)Math.Cos(angle) * speed;
            shard.VelocityY = (float)Math.Sin(angle) * speed;
            shard.Life = shard.MaxLife = 42 + random.Next(18);
            shard.IsDecayShard = true;
            shard.Color = new[] { Color.FromArgb(255, 66, 52), Color.FromArgb(255, 105, 72), Color.FromArgb(235, 50, 45) }[random.Next(3)];
            particles.Add(shard);

            // A matching transparent child-control shard is deliberately above
            // the HUD labels, so special-decay fragments cannot be hidden by
            // the Total control itself.
            FloatingEffect overlay = new FloatingEffect();
            overlay.Burst = true; overlay.FrontControl = true;
            overlay.X = shard.X; overlay.Y = shard.Y;
            overlay.VelocityX = shard.VelocityX * 1.18F; overlay.VelocityY = shard.VelocityY * 1.18F;
            overlay.Life = overlay.MaxLife = shard.MaxLife;
            overlay.CriticalColor = shard.Color;
            overlay.Label = new DecayShardLabel(shard.Color, 13 + strength);
            overlay.Label.MouseDown += WindowMouseDown;
            Controls.Add(overlay.Label); overlay.Label.BringToFront();
            floatingEffects.Add(overlay);
        }
    }
    private void UpdateParticles()
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle particle = particles[i];
            particle.X += particle.VelocityX;
            particle.Y += particle.VelocityY;
            particle.VelocityY += particle.IsStar ? 0.025F : particle.IsDecayShard ? 0.055F : 0.18F;
            particle.Life--;
            if (particle.Life <= 0) particles.RemoveAt(i);
        }
    }
    private void SpawnFloatingEffect(bool increasing)
    {
        FloatingEffect effect = new FloatingEffect();
        effect.Increasing = increasing;
        bool leftSide = random.Next(2) == 0;
        effect.X = leftSide ? totalBaseLocation.X - 3 : totalBaseLocation.X + totalLabel.Width - 8;
        effect.Y = totalBaseLocation.Y + random.Next(3, 29);
        int direction = random.Next(3) - 1; // up, horizontal, down
        float speed = increasing ? 1.2F + (float)random.NextDouble() * 1.2F : 1.4F + (float)random.NextDouble() * 1.2F;
        effect.VelocityX = (leftSide ? -speed : speed) * 2F;
        effect.VelocityY = direction * (0.7F + (float)random.NextDouble() * 1.1F) * 2F;
        effect.Life = effect.MaxLife = increasing ? 180 : 210;
        effect.Label = new EffectLabel { Text = increasing ? RandomIncreaseEffect(leftSide, direction) : "-1", AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", increasing ? 11F : 9F), ForeColor = increasing ? RandomEffectColor() : Color.FromArgb(135, 190, 255) };
        floatingEffects.Add(effect);
    }
    private void SpawnEchoCascadeEffect(int delta)
    {
        FloatingEffect effect = new FloatingEffect();
        effect.Increasing = true; effect.EchoCascade = true;
        effect.X = totalBaseLocation.X + totalLabel.Width - 5;
        effect.Y = totalBaseLocation.Y + random.Next(3, 18);
        float horizontal = 1.0F, vertical = -1.15F;
        switch (activeElement)
        {
            case ElementStyle.Water: horizontal = 0.55F; vertical = -0.65F; break;
            case ElementStyle.Wind: horizontal = 2.05F; vertical = -0.18F; break;
            case ElementStyle.Earth: horizontal = 0.28F; vertical = -0.88F; break;
            case ElementStyle.Lightning: horizontal = 2.25F; vertical = -1.45F; break;
            case ElementStyle.Ice: horizontal = 0.75F; vertical = -1.22F; break;
            case ElementStyle.Nature: horizontal = 1.1F; vertical = -0.72F; break;
            case ElementStyle.Shadow: horizontal = 0.42F; vertical = -0.5F; break;
            case ElementStyle.Cosmic: horizontal = 1.3F; vertical = -1.0F; break;
        }
        effect.VelocityX = horizontal; effect.VelocityY = vertical;
        effect.Life = effect.MaxLife = 160;
        effect.CriticalColor = AuraStyleColor();
        effect.Label = new EffectLabel { Text = "+" + delta, AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 11F), ForeColor = effect.CriticalColor };
        floatingEffects.Add(effect);
    }
    private void SpawnCriticalEffect(int multiplier)
    {
        double angle = -Math.PI / 2 + random.NextDouble() * Math.PI / 2;
        float intensity = multiplier - 1;
        FloatingEffect effect = new FloatingEffect();
        effect.Increasing = true;
        effect.Critical = true;
        effect.Multiplier = multiplier;
        effect.X = totalBaseLocation.X + 42 + (float)Math.Cos(angle) * 15;
        effect.Y = totalBaseLocation.Y + 19 + (float)Math.Sin(angle) * 10;
        effect.VelocityX = (0.7F + (float)Math.Cos(angle) * (0.8F + intensity * 0.25F)) * 2F;
        effect.VelocityY = (-1.1F - (float)Math.Abs(Math.Sin(angle)) * (0.9F + intensity * 0.25F)) * 2F;
        effect.Life = effect.MaxLife = 210 + multiplier * 12;
        Color colour = CriticalColor(multiplier);
        CriticalLabel label = new CriticalLabel();
        label.Text = "CRIT +" + multiplier;
        label.AutoSize = true;
        label.BackColor = Color.Transparent;
        label.Font = new Font("Segoe UI Black", 9F + multiplier * 1.8F);
        label.ForeColor = colour;
        effect.Label = label;
        effect.CriticalColor = colour;
        floatingEffects.Add(effect);
        for (int i = 0; i < multiplier; i++) SpawnCriticalBurst(effect.X, effect.Y, multiplier, colour);
    }
    private void SpawnCriticalBurst(float x, float y, int multiplier, Color colour)
    {
        double angle = random.NextDouble() * Math.PI * 2;
        FloatingEffect burst = new FloatingEffect();
        burst.Increasing = true;
        burst.Burst = true;
        burst.X = x + random.Next(-8, 9);
        burst.Y = y + random.Next(-6, 7);
        burst.VelocityX = (float)Math.Cos(angle) * (0.7F + multiplier * 0.25F) * 2F;
        burst.VelocityY = ((float)Math.Sin(angle) * (0.7F + multiplier * 0.25F) - 0.5F) * 2F;
        burst.Life = burst.MaxLife = 105 + multiplier * 15;
        burst.CriticalColor = colour;
        burst.Label = new EffectLabel { Text = iBurstGlyph(random.Next(3)), AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8F + multiplier), ForeColor = colour };
        floatingEffects.Add(burst);
    }
    private void SpawnRewindEffect()
    {
        foreach (FloatingEffect activeEffect in floatingEffects)
            if (activeEffect.Rewind) return;
        FloatingEffect effect = new FloatingEffect();
        effect.Increasing = true; effect.Critical = true; effect.Rewind = true; effect.Multiplier = 5;
        // It bursts out of the Total first, then flies along a random heading from
        // clock positions 11 through 4 before easing to a stop.
        double rewindAngle = (-120.0 + random.NextDouble() * 150.0) * Math.PI / 180.0;
        effect.X = totalBaseLocation.X + 2F; effect.Y = totalBaseLocation.Y - 20F;
        effect.VelocityX = (float)Math.Cos(rewindAngle) * 0.75F;
        effect.VelocityY = (float)Math.Sin(rewindAngle) * 0.75F;
        // 86 frames x 35 ms is approximately three seconds. It stays crisp for
        // most of the flight, then fades during the final fraction of a second.
        effect.Life = effect.MaxLife = 86;
        effect.CriticalColor = Color.FromArgb(145, 255, 220);
        CriticalLabel label = new CriticalLabel { Text = "REWIND!", AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI Black", 20F), ForeColor = effect.CriticalColor };
        effect.Label = label; floatingEffects.Add(effect);
        for (int i = 0; i < 6; i++) SpawnCriticalBurst(effect.X, effect.Y, 5, effect.CriticalColor);
    }
    private static string iBurstGlyph(int index) { return index == 0 ? "✦" : index == 1 ? "✹" : "✧"; }
    private static Color CriticalColor(int multiplier)
    {
        if (multiplier == 2) return Color.FromArgb(255, 235, 65);
        if (multiplier == 3) return Color.FromArgb(255, 158, 50);
        if (multiplier == 4) return Color.FromArgb(255, 82, 35);
        return Color.FromArgb(255, 45, 45);
    }
    private void UpdateFloatingEffects()
    {
        for (int i = floatingEffects.Count - 1; i >= 0; i--)
        {
            FloatingEffect effect = floatingEffects[i];
            effect.X += effect.VelocityX;
            effect.Y += effect.VelocityY;
            if (!effect.Rewind)
            {
                if (effect.Increasing) effect.VelocityY += 0.03F;
                else effect.VelocityY += 0.02F;
            }
            else
            {
                // A quick launch that naturally settles instead of looking static.
                effect.VelocityX *= 0.985F;
                effect.VelocityY *= 0.985F;
            }
            effect.Life--;
            Size textSize = TextRenderer.MeasureText(effect.Label.Text, effect.Label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            Rectangle textBounds = new Rectangle((int)effect.X, (int)effect.Y, textSize.Width, textSize.Height);
            if (effect.FrontControl) effect.Label.Location = textBounds.Location;
            // REWIND is deliberately placed in the clear flight area, so retain it
            // for its full duration instead of deleting it on a direction check.
            if (!effect.FrontControl && !effect.Rewind && HitsProtectedLabel(textBounds)) effect.Life = 0;
            double visibility = effect.Rewind && effect.Life > 18 ? 1.0 : effect.Rewind ? effect.Life / 18.0 : effect.Life / (double)effect.MaxLife;
            Color target = effect.Critical || effect.Burst || effect.EchoCascade ? effect.CriticalColor : effect.Increasing ? Color.White : Color.FromArgb(135, 190, 255);
            effect.Label.ForeColor = Blend(BackColor, target, visibility);
            if (effect.Critical && !effect.Rewind)
            {
                CriticalLabel criticalLabel = effect.Label as CriticalLabel;
                float size = (9F + effect.Multiplier * 1.8F) * (0.82F + (float)visibility * 0.18F);
                if (Math.Abs(effect.Label.Font.Size - size) > 0.15F) { Font oldFont = effect.Label.Font; effect.Label.Font = new Font("Segoe UI Black", size); oldFont.Dispose(); }
            }
            if (effect.Life <= 0)
            {
                if (effect.FrontControl && Controls.Contains(effect.Label)) Controls.Remove(effect.Label);
                effect.Label.Dispose();
                floatingEffects.RemoveAt(i);
            }
        }
    }
    private bool HitsProtectedLabel(Rectangle effectBounds)
    {
        return effectBounds.IntersectsWith(detailLabel.Bounds) || effectBounds.IntersectsWith(highScoreLabel.Bounds) || effectBounds.IntersectsWith(allTimeLabel.Bounds) || effectBounds.IntersectsWith(totalHighScoreLabel.Bounds) || effectBounds.Right >= ClientSize.Width || effectBounds.Left <= 0 || effectBounds.Top <= 0 || effectBounds.Bottom >= ClientSize.Height;
    }
    private string RandomIncreaseEffect(bool leftSide, int verticalDirection)
    {
        string[] effects = { "✦", "✧", "✹", "✷", "✺", "✵", "⋆", "✸", "+1" };
        if (random.Next(10) < 9) return effects[random.Next(effects.Length)];
        if (leftSide) return verticalDirection < 0 ? "↖" : verticalDirection > 0 ? "↙" : "←";
        return verticalDirection < 0 ? "↗" : verticalDirection > 0 ? "↘" : "→";
    }
    private Color RandomEffectColor()
    {
        Color[] colors = { Color.White, Color.FromArgb(145, 205, 255), Color.FromArgb(105, 170, 255), Color.FromArgb(190, 225, 255) };
        return colors[random.Next(colors.Length)];
    }
    private void DrawParticles(Graphics graphics, bool frontLayer)
    {
        foreach (Particle particle in particles)
        {
            if (particle.IsDecayShard != frontLayer) continue;
            int alpha = (int)(220 * particle.Life / particle.MaxLife);
            Color baseColour = particle.Color;
            if (particle.IsSpecial)
            {
                bool brightPhase = (Environment.TickCount / 80) % 2 == 0;
                baseColour = brightPhase ? Color.FromArgb(92, 255, 168) : Color.FromArgb(255, 78, 64);
            }
            Color colour = Color.FromArgb(alpha, baseColour);
            if (particle.IsStar)
            {
                PointF[] points = new PointF[8];
                for (int i = 0; i < 8; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 4;
                    float radius = (i % 2 == 0) ? 3.0F : 1.2F;
                    points[i] = new PointF(particle.X + (float)Math.Cos(angle) * radius, particle.Y + (float)Math.Sin(angle) * radius);
                }
                using (SolidBrush brush = new SolidBrush(colour)) graphics.FillPolygon(brush, points);
            }
            else if (particle.IsDecayShard)
            {
                float length = 5.2F + Math.Abs(particle.VelocityX) * 1.4F;
                float width = 2.1F;
                PointF[] shard =
                {
                    new PointF(particle.X - length, particle.Y), new PointF(particle.X, particle.Y - width),
                    new PointF(particle.X + length, particle.Y), new PointF(particle.X, particle.Y + width)
                };
                using (Pen outline = new Pen(Color.FromArgb(Math.Min(255, alpha + 20), 255, 190, 165), 0.85F)) graphics.DrawPolygon(outline, shard);
                using (SolidBrush brush = new SolidBrush(colour)) graphics.FillPolygon(brush, shard);
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(colour)) graphics.FillEllipse(brush, particle.X - 1.5F, particle.Y - 1.5F, 3, 3);
            }
        }
    }
    private void DrawFloatingEffects(Graphics graphics)
    {
        foreach (FloatingEffect effect in floatingEffects)
            TextRenderer.DrawText(graphics, effect.Label.Text, effect.Label.Font, new Point((int)effect.X, (int)effect.Y), effect.Label.ForeColor, TextFormatFlags.NoPadding);
    }
    private void UpdateBubble()
    {
        DateTime now = DateTime.UtcNow;
        if (planetGachaActive)
        {
            // Three seconds of rolling followed by a ten-second readable result.
            if (now < planetGachaStartedAt.AddSeconds(13)) return;
            planetGachaActive = false;
            if (queuedPlanetGacha.Count > 0) { StartPlanetGacha(queuedPlanetGacha.Dequeue()); return; }
            if (queuedRareBubbles.Count > 0) { ApplyRareBubble(queuedRareBubbles.Dequeue()); return; }
            SetNormalBubble(now);
            return;
        }
        if (bubbleShowsRareChance && now < bubbleRareUntil) return;
        if (bubbleShowsRareChance)
        {
            bubbleShowsRareChance = false;
            bubbleFlashColorA = Color.White;
            bubbleFlashColorB = Color.FromArgb(120, 195, 255);
            SetNormalBubble(now);
            return;
        }
        int liveRate = recentInputs.Count;
        if (liveRate > 10)
        {
            string rhythmMessage = "Rhythm " + liveRate + " key/sec";
            if (bubbleText != rhythmMessage)
            {
                bubbleText = rhythmMessage;
                bubbleAppearedAt = now;
            }
            nextBubbleUpdateAt = now.AddSeconds(2);
            return;
        }
        if (now < nextBubbleUpdateAt) return;
        SetNormalBubble(now);
    }
    private void SetNormalBubble(DateTime now)
    {
        int rate = recentInputs.Count;
        List<string> normalMessages = new List<string>();
        normalMessages.Add("Core calm"); normalMessages.Add("Orbit merging"); normalMessages.Add("Rhythm " + rate + " key/sec");
        normalMessages.Add("Waiting for input"); normalMessages.Add("Total " + CompactBubbleNumber(totalCount));
        if (dailyPlanets.Count > 0) normalMessages.Add("Planets: " + dailyPlanets.Count + " / 8");
        bubbleText = normalMessages[random.Next(normalMessages.Count)];
        bubbleAppearedAt = now;
        nextBubbleUpdateAt = now.AddSeconds(2 + random.NextDouble() * 8);
    }
    private void ShowRareEventBubble(string eventName, double chancePercent, bool inputDriven)
    {
        Dictionary<string, long> counters = inputDriven ? inputEventKeyPressCounts : idleEventDecayedKeyCounts;
        long count = counters.ContainsKey(eventName) ? counters[eventName] : 0;
        RareBubbleMessage message = new RareBubbleMessage();
        // A fixed compact grammar keeps every special event at a readable size
        // inside the narrow, single-line bubble: event chance attempt-unit.
        message.Text = eventName.Replace(" ", "") + " " + CompactChance(chancePercent) + "%#" + CompactBubbleNumber(count);
        if (counters.ContainsKey(eventName)) counters[eventName] = 0;
        if (eventName.StartsWith("CRIT"))
        {
            message.ColorA = Color.FromArgb(255, 238, 72); message.ColorB = Color.FromArgb(255, 150, 42);
        }
        else if (eventName.StartsWith("DECAY"))
        {
            message.ColorA = Color.FromArgb(85, 245, 180); message.ColorB = Color.FromArgb(85, 175, 255);
        }
        else if (eventName.StartsWith("ECHO"))
        {
            message.ColorA = Color.FromArgb(238, 184, 112); message.ColorB = Color.FromArgb(255, 220, 171);
        }
        else
        {
            message.ColorA = Color.White; message.ColorB = Color.FromArgb(120, 195, 255);
        }
        if (planetGachaActive) { queuedRareBubbles.Enqueue(message); return; }
        ApplyRareBubble(message);
    }
    private static string CompactChance(double chance)
    {
        // Preserve the third decimal for the 0.001% Rewind chance rather
        // than rounding it away in the compact single-line bubble.
        string text = chance.ToString(chance < 0.01 ? "0.###" : "0.##");
        return text.Length > 1 && text[0] == '0' && !char.IsDigit(text[1]) ? text.Substring(1) : text;
    }
    private static string CompactBubbleNumber(long value)
    {
        double absolute = Math.Abs((double)value);
        string suffix = String.Empty;
        double scaled = value;
        if (absolute >= 1000000000D) { scaled = value / 1000000000D; suffix = "B"; }
        else if (absolute >= 1000000D) { scaled = value / 1000000D; suffix = "M"; }
        else if (absolute >= 1000D) { scaled = value / 1000D; suffix = "k"; }
        return suffix.Length == 0 ? value.ToString() : Math.Round(scaled).ToString("0") + suffix;
    }
    private void ApplyRareBubble(RareBubbleMessage message)
    {
        bubbleText = message.Text;
        bubbleFlashColorA = message.ColorA;
        bubbleFlashColorB = message.ColorB;
        bubbleShowsRareChance = true;
        bubbleAppearedAt = DateTime.UtcNow;
        bubbleRareUntil = bubbleAppearedAt.AddSeconds(10);
    }
    private void DrawBubble(Graphics graphics)
    {
        if (planetGachaActive) { DrawPlanetGachaBubble(graphics); return; }
        DateTime now = DateTime.UtcNow;
        double enter = Math.Min(1.0, Math.Max(0.0, (now - bubbleAppearedAt).TotalMilliseconds / 180.0));
        float yOffset = (float)((1.0 - enter) * 6.0);
        float fontSize = bubbleShowsRareChance ? 8.5F : 8F;
        FontStyle fontStyle = bubbleShowsRareChance ? FontStyle.Bold : FontStyle.Regular;
        // The bubble stays inside the same narrow HUD column.
        float maximumBubbleWidth = HudWidth;
        const float horizontalPadding = 16F;
        Font measureFont = null;
        Size measured = Size.Empty;
        while (true)
        {
            measureFont = new Font("Segoe UI", fontSize, fontStyle);
            measured = TextRenderer.MeasureText(bubbleText, measureFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            // Rare-event text is intentionally kept as a single line.  The
            // compact HUD therefore allows a smaller final font rather than
            // clipping the right edge or creating a second line.
            if (measured.Width + horizontalPadding <= maximumBubbleWidth || fontSize <= 7F) break;
            measureFont.Dispose();
            fontSize -= 0.25F;
        }
        using (measureFont)
        {
            float width = Math.Min(maximumBubbleWidth, Math.Max(104F, measured.Width + horizontalPadding));
            float height = Math.Max(35F, measured.Height + 14F);
            // Flush with the HUD's left edge, above the HUD, and stopping before
            // the Living Core. One line is always drawn in the bubble body.
            RectangleF bounds = new RectangleF((HudWidth - width) / 2F, hudTop - 5F - height - yOffset, width, height);
            DrawBubbleBody(graphics, bounds, measureFont);
        }
    }
    private void DrawPlanetGachaBubble(Graphics graphics)
    {
        double elapsed = (DateTime.UtcNow - planetGachaStartedAt).TotalSeconds;
        bool rolling = elapsed < 3.0;
        string shown = planetGachaResult;
        if (rolling)
        {
            List<string> choices = new List<string>();
            foreach (string planet in PlanetNames) if (!dailyPlanets.Contains(planet) || planet == planetGachaResult) choices.Add(planet);
            if (choices.Count > 0) shown = choices[((int)(elapsed * 9.0)) % choices.Count];
        }
        // A short HUD has less empty space above the card.  Keep the entire
        // gacha panel inside that space instead of allowing its top edge to
        // use a negative Y coordinate (which clips the title).
        float bubbleHeight = Math.Min(64F, Math.Max(54F, hudTop - 5F));
        float bubbleTop = Math.Max(0F, hudTop - 5F - bubbleHeight);
        RectangleF bounds = new RectangleF(0F, bubbleTop, HudWidth, bubbleHeight);
        Color accent = rolling ? Color.FromArgb(155, 215, 255) : PlanetColor(planetGachaResult);
        using (System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedPath(bounds, 10F))
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, 28, 42, 67)))
        using (Pen outline = new Pen(Color.FromArgb(230, accent), rolling ? 1.0F : 1.5F))
        using (Font title = new Font("Segoe UI", 8.5F, FontStyle.Bold))
        using (Font result = new Font("Segoe UI", 9F, FontStyle.Bold))
        {
            graphics.FillPath(brush, path); graphics.DrawPath(outline, path);
            TextRenderer.DrawText(graphics, "PLANET GACHA", title, new Rectangle(6, (int)bounds.Top + 3, HudWidth - 12, 17), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string line = rolling ? shown : "NEW: " + shown;
            TextRenderer.DrawText(graphics, line, result, new Rectangle(5, (int)bounds.Top + 22, HudWidth - 10, (int)bounds.Height - 25), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        PointF[] tail = { new PointF(bounds.Right - 31F, bounds.Bottom), new PointF(bounds.Right - 18F, bounds.Bottom), new PointF(bounds.Right - 24F, bounds.Bottom + 4F) };
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, 28, 42, 67))) graphics.FillPolygon(brush, tail);
    }
    private void DrawBubbleBody(Graphics graphics, RectangleF bounds, Font font)
    {
        DateTime now = DateTime.UtcNow;
        bool flashBright = !bubbleShowsRareChance || ((long)(now - bubbleAppearedAt).TotalMilliseconds / 500) % 2 == 0;
        Color fill = bubbleShowsRareChance
            ? (flashBright ? Color.FromArgb(225, 48, 68, 112) : Color.FromArgb(145, 24, 38, 72))
            : Color.FromArgb(190, 28, 42, 67);
        Color outlineColor = bubbleShowsRareChance
            ? Color.FromArgb(flashBright ? 230 : 155, flashBright ? bubbleFlashColorA.R : bubbleFlashColorB.R, flashBright ? bubbleFlashColorA.G : bubbleFlashColorB.G, flashBright ? bubbleFlashColorA.B : bubbleFlashColorB.B)
            : Color.FromArgb(155, 125, 190, 255);
        using (System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedPath(bounds, 9F))
        using (SolidBrush brush = new SolidBrush(fill))
        using (Pen outline = new Pen(outlineColor, flashBright && bubbleShowsRareChance ? 1.25F : 0.8F))
        {
            graphics.FillPath(brush, path);
            graphics.DrawPath(outline, path);
        }
        PointF[] tail = { new PointF(bounds.Right, bounds.Top + 18F), new PointF(bounds.Right, bounds.Top + 28F), new PointF(bounds.Right + 12F, bounds.Top + 25F) };
        using (SolidBrush brush = new SolidBrush(fill)) graphics.FillPolygon(brush, tail);
        Color textColor = flashBright ? bubbleFlashColorA : bubbleFlashColorB;
        Rectangle textBounds = Rectangle.Round(new RectangleF(bounds.X + 8F, bounds.Y + 5F, bounds.Width - 16F, bounds.Height - 10F));
        TextRenderer.DrawText(graphics, bubbleText, font, textBounds, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        float diameter = radius * 2F;
        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
    private PointF AuraCoreCenter() { return new PointF(AuraCoreCoordinate, AuraCoreCoordinate); }
    private double AuraMemoryProgress() { return 1.0 - Math.Exp(-Math.Log(1.0 + Math.Max(0, allTimeTotal)) / 6.0); }
    private static double GetParticleSettleProgress(AuraParticle particle)
    {
        return Math.Min(1.0, Math.Max(0.0, (DateTime.UtcNow - particle.CreatedAt).TotalSeconds / particle.SettleDurationSeconds));
    }
    private Color RandomAuraParticleColor()
    {
        Color[] colours =
        {
            Color.FromArgb(250, 252, 255), Color.FromArgb(215, 235, 255),
            Color.FromArgb(170, 215, 255), Color.FromArgb(105, 185, 255),
            Color.FromArgb(55, 145, 255), Color.FromArgb(25, 100, 225),
            Color.FromArgb(12, 62, 168)
        };
        return colours[random.Next(colours.Length)];
    }
    private void SpawnAuraInputParticle()
    {
        PointF core = AuraCoreCenter();
        double angle = random.NextDouble() * Math.PI * 2.0;
        float distance = AuraReferenceWindowSize * (2F + (float)random.NextDouble() * 2F);
        float x = core.X + (float)Math.Cos(angle) * distance;
        float y = core.Y + (float)Math.Sin(angle) * distance;
        x = Math.Max(6F, Math.Min(AuraWindowSize - 6F, x));
        y = Math.Max(6F, Math.Min(AuraWindowSize - 6F, y));
        auraParticles.Add(new AuraParticle
        {
            X = x, Y = y, PreviousX = x, PreviousY = y,
            OrbitAngle = random.NextDouble() * Math.PI * 2.0, OrbitTilt = random.NextDouble() * Math.PI * 2.0, OrbitDirection = random.Next(2) == 0 ? -1 : 1,
            OrbitRadius = 16F + random.Next(29),
            Life = 330, MaxLife = 330, CreatedAt = DateTime.UtcNow, SettleDurationSeconds = 60 + random.Next(541), SpeedTier = random.Next(25, 101), AuraColor = RandomAuraParticleColor(), Entering = true, Variant = random.Next(10)
        });
        auraParticles[auraParticles.Count - 1].BaseOrbitRadius = auraParticles[auraParticles.Count - 1].OrbitRadius;
    }
    private void SpawnAuraInputParticle(Color? colour)
    {
        int before = auraParticles.Count;
        SpawnAuraInputParticle();
        if (auraParticles.Count > before && colour.HasValue) auraParticles[auraParticles.Count - 1].AuraColor = colour.Value;
    }
    private void ReplaceOldestAuraParticleWithIncoming(Color? colour)
    {
        if (auraParticles.Count == 0) { SpawnAuraInputParticle(colour); return; }
        int oldestIndex = 0;
        DateTime oldestCreatedAt = auraParticles[0].CreatedAt;
        for (int i = 1; i < auraParticles.Count; i++)
        {
            if (auraParticles[i].CreatedAt < oldestCreatedAt)
            {
                oldestCreatedAt = auraParticles[i].CreatedAt;
                oldestIndex = i;
            }
        }
        auraParticles.RemoveAt(oldestIndex);
        SpawnAuraInputParticle(colour);
    }
    private void RefreshIncomingAuraParticles(int countBefore, int arrivals, Color? colour)
    {
        if (totalCount <= 0 || arrivals <= 0) return;
        int alreadyAdded = Math.Max(0, auraParticles.Count - countBefore);
        int remaining = Math.Max(0, arrivals - alreadyAdded);
        for (int i = 0; i < remaining && auraParticles.Count >= MaxRenderedAuraParticles; i++)
            ReplaceOldestAuraParticleWithIncoming(colour);
    }
    private void ActivateDecayAuraRedEffect()
    {
        auraDecayRedUntil = DateTime.UtcNow.AddSeconds(3);
        int targetCount = (int)Math.Ceiling(auraParticles.Count * 0.80);
        for (int i = 0; i < auraParticles.Count; i++) auraParticles[i].DecayRedTarget = false;
        // Partial Fisher-Yates selection gives 80% of the active
        // particles a stable red target for this one decay event.
        List<int> indices = new List<int>();
        for (int i = 0; i < auraParticles.Count; i++) indices.Add(i);
        for (int i = 0; i < targetCount; i++)
        {
            int pick = i + random.Next(indices.Count - i);
            int chosen = indices[pick]; indices[pick] = indices[i]; indices[i] = chosen;
            auraParticles[chosen].DecayRedTarget = true;
        }
    }
    private void ActivateRewindAuraGreenEffect()
    {
        auraRewindGreenUntil = DateTime.UtcNow.AddSeconds(3);
        int targetCount = (int)Math.Ceiling(auraParticles.Count * 0.80);
        for (int i = 0; i < auraParticles.Count; i++) auraParticles[i].RewindGreenTarget = false;
        List<int> indices = new List<int>();
        for (int i = 0; i < auraParticles.Count; i++) indices.Add(i);
        for (int i = 0; i < targetCount; i++)
        {
            int pick = i + random.Next(indices.Count - i);
            int chosen = indices[pick]; indices[pick] = indices[i]; indices[i] = chosen;
            auraParticles[chosen].RewindGreenTarget = true;
        }
    }
    private void SyncAuraParticlesToScore(Color? newParticleColour = null)
    {
        // The score remains exact, but the visual renderer is safely bounded.
        // Core mass/rings still reflect the full Total without allocating or
        // drawing thousands of individual GDI shapes.
        int target = totalCount <= 0 ? 0 : (int)Math.Min(MaxRenderedAuraParticles, totalCount);
        while (auraParticles.Count < target) SpawnAuraInputParticle(newParticleColour);
        while (auraParticles.Count > target) auraParticles.RemoveAt(auraParticles.Count - 1);
    }
    private void UpdateTypingAura()
    {
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-1);
        while (recentInputs.Count > 0 && recentInputs.Peek() < cutoff) recentInputs.Dequeue();
        int keysPerSecond = recentInputs.Count;
        if (keysPerSecond > sessionPeakRate) sessionPeakRate = keysPerSecond;
        double targetSpeed = 1.0 - Math.Exp(-keysPerSecond / 7.0);
        double targetMass = 1.0 - Math.Exp(-Math.Log(1.0 + Math.Abs(totalCount)) / 5.0);
        if (totalCount < 0) targetMass *= 0.82;
        auraSpeed += (targetSpeed - auraSpeed) * 0.09;
        auraMass += (targetMass - auraMass) * 0.045;
        auraPhase += 0.035 + auraSpeed * 0.15;
        bool quiet = DateTime.UtcNow - lastInputAt > TimeSpan.FromSeconds(1.1);
        PointF core = AuraCoreCenter();
        for (int i = auraParticles.Count - 1; i >= 0; i--)
        {
            AuraParticle particle = auraParticles[i];
            // Tier 1 through 100 is discrete. The normalizing constant keeps
            // tier 100 visibly fast while retaining an exact 100:1 ratio.
            float motionFactor = particle.SpeedTier * 0.0248F;
            particle.PreviousX = particle.X; particle.PreviousY = particle.Y;
            if (particle.Entering)
            {
                float dx = core.X - particle.X, dy = core.Y - particle.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                // Incoming particles must be visible for every tier: even the
                // slowest blue mote reaches the Core in a few seconds, while
                // fast tiers form a short, energetic arrival trail.
                float pull = 0.035F + (0.045F + (float)auraSpeed * 0.115F) * motionFactor;
                particle.X += dx * pull; particle.Y += dy * pull;
                if (distance < 13F) particle.Entering = false;
            }
            else
            {
                // Each particle has its own ten-minute settling age. During a
                // quiet period it gradually joins the compact blue-dot ring.
                double particleSettle = GetParticleSettleProgress(particle);
                float compactRadius = 8F + (float)auraMass * 2F;
                float desiredRadius = quiet
                    ? particle.BaseOrbitRadius * (float)(1.0 - particleSettle) + compactRadius * (float)particleSettle
                    : particle.BaseOrbitRadius;
                particle.OrbitRadius += (desiredRadius - particle.OrbitRadius) * 0.12F;
                particle.OrbitAngle += (0.025 + auraSpeed * 0.13 + (particle.Variant % 3) * 0.004) * motionFactor * particle.OrbitDirection;
                // Fast tiers can overshoot radially, but every orbit remains
                // geometrically circular regardless of speed or direction.
                float excessSpeed = Math.Max(0F, motionFactor - 0.80F);
                float maxExcursion = Math.Min(110F, excessSpeed * 65F);
                particle.RadialVelocity += (float)Math.Sin(particle.OrbitAngle * 1.7 + auraPhase * 0.6 + particle.Variant) * excessSpeed * 0.31F;
                particle.RadialVelocity -= particle.RadialOffset * 0.018F;
                particle.RadialVelocity *= 0.965F;
                particle.RadialOffset += particle.RadialVelocity;
                particle.RadialOffset = Math.Max(-maxExcursion, Math.Min(maxExcursion, particle.RadialOffset));
                float activeRadius = particle.OrbitRadius + particle.RadialOffset;
                float localX = (float)Math.Cos(particle.OrbitAngle) * activeRadius;
                float localY = (float)Math.Sin(particle.OrbitAngle) * activeRadius;
                float tiltCos = (float)Math.Cos(particle.OrbitTilt), tiltSin = (float)Math.Sin(particle.OrbitTilt);
                float desiredX = core.X + localX * tiltCos - localY * tiltSin;
                float desiredY = core.Y + localX * tiltSin + localY * tiltCos;
                float follow = Math.Min(0.48F, (0.13F + (float)auraSpeed * 0.12F) * (0.72F + motionFactor * 0.28F));
                particle.X += (desiredX - particle.X) * follow;
                particle.Y += (desiredY - particle.Y) * follow;
                particle.X = Math.Max(4F, Math.Min(AuraWindowSize - 4F, particle.X));
                particle.Y = Math.Max(4F, Math.Min(AuraWindowSize - 4F, particle.Y));
            }
            // A particle remains while its matching Total point exists.
        }
        if (auraWindow != null && !auraWindow.IsDisposed) auraWindow.Invalidate();
    }
    private Color AuraStyleColor()
    {
        switch (activeElement)
        {
            case ElementStyle.Fire: return Color.FromArgb(255, 152, 72);
            case ElementStyle.Water: return Color.FromArgb(105, 205, 255);
            case ElementStyle.Wind: return Color.FromArgb(170, 230, 255);
            case ElementStyle.Earth: return Color.FromArgb(220, 182, 112);
            case ElementStyle.Lightning: return Color.FromArgb(210, 170, 255);
            case ElementStyle.Ice: return Color.FromArgb(185, 240, 255);
            case ElementStyle.Nature: return Color.FromArgb(130, 235, 165);
            case ElementStyle.Shadow: return Color.FromArgb(190, 125, 255);
            case ElementStyle.Cosmic: return Color.FromArgb(155, 175, 255);
            default: return Color.FromArgb(255, 235, 172);
        }
    }
    private void DrawTypingAura(Graphics graphics)
    {
        System.Drawing.Drawing2D.SmoothingMode oldSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        PointF core = AuraCoreCenter();
        double memory = AuraMemoryProgress();
        // The Living Core itself remains neutral white. Element selection controls
        // particle shape/path only, keeping rewind from tinting the Core green.
        Color styleTint = Color.FromArgb(80, 185, 255);
        Color coreTint = Color.White;
        float baseRadius = 5.5F + (float)auraMass * 7.5F;
        float pulse = 1F + (float)Math.Sin(auraPhase * 2.2) * (0.035F + (float)auraSpeed * 0.12F);
        float radius = baseRadius * pulse;

        // Permanent all-time memory appears as increasingly visible peripheral motes,
        // not as a score modifier or a hard milestone unlock.
        for (int i = 0; i < 7; i++)
        {
            double richness = Math.Pow(memory, 1.0 + i * 0.24);
            if (richness < 0.025) continue;
            double angle = auraPhase * (0.35 + i * 0.06) + i * 0.91;
            float orbit = radius + 25F + i * 4.2F;
            float x = core.X + (float)Math.Cos(angle) * orbit;
            float y = core.Y + (float)Math.Sin(angle) * orbit * 0.52F;
            int alpha = (int)(richness * 135);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, styleTint))) graphics.FillEllipse(brush, x - 1.25F, y - 1.25F, 2.5F, 2.5F);
        }
        for (int glow = 4; glow >= 1; glow--)
        {
            float glowRadius = radius + glow * 3.4F;
            int alpha = (int)((8 + auraSpeed * 16) / glow);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, coreTint))) graphics.FillEllipse(brush, core.X - glowRadius, core.Y - glowRadius, glowRadius * 2F, glowRadius * 2F);
        }
        using (SolidBrush coreBrush = new SolidBrush(Color.FromArgb(235, coreTint))) graphics.FillEllipse(coreBrush, core.X - radius, core.Y - radius, radius * 2F, radius * 2F);
        using (SolidBrush highlight = new SolidBrush(Color.FromArgb(225, Color.White))) graphics.FillEllipse(highlight, core.X - radius * 0.36F, core.Y - radius * 0.42F, radius * 0.52F, radius * 0.52F);
        foreach (AuraParticle particle in auraParticles) DrawAuraParticle(graphics, particle, styleTint, coreTint);
        // Wind is intentionally drawn over the particle cloud so rapid input
        // remains clearly readable instead of disappearing behind 1,000 dots.
        DrawSpeedWind(graphics, core, radius);
        DrawOwnedPlanets(graphics, core);
        graphics.SmoothingMode = oldSmoothing;
    }
    private void DrawSpeedWind(Graphics graphics, PointF core, float coreRadius)
    {
        int keyRate = recentInputs.Count;
        if (keyRate < 3) return;
        // Each currently measured key per second produces four visible wind
        // strokes.  The cap is only a renderer safeguard for extreme bursts.
        int lineCount = Math.Min(160, keyRate * 4);
        Color[] windColours = { Color.White, Color.FromArgb(205, 232, 255), Color.FromArgb(145, 205, 255), Color.FromArgb(88, 168, 255) };
        for (int i = 0; i < lineCount; i++)
        {
            // Alternate rotational flow so the wind never appears locked to
            // one clockwise-only direction.
            int rotationDirection = (i & 1) == 0 ? 1 : -1;
            double angle = rotationDirection * auraPhase * (1.65 + i * 0.035) + i * Math.PI * 2.0 / lineCount;
            // Give each wind stroke a stable, non-uniform radial lane.  The
            // anchor can be inside the Core (0.5x) or well outside it (3x),
            // producing a genuinely scattered field instead of a tight ring.
            double radialNoise = Math.Sin((i + 1) * 78.233) * 43758.5453;
            radialNoise -= Math.Floor(radialNoise);
            float orbit = coreRadius * (0.5F + 2.5F * (float)radialNoise);
            float radialX = (float)Math.Cos(angle), radialY = (float)Math.Sin(angle);
            float x = core.X + radialX * orbit;
            float y = core.Y + radialY * orbit;
            float length = 10F + Math.Min(18F, keyRate * 0.7F) + (i % 3) * 2F;
            float tangentX = (float)-Math.Sin(angle) * rotationDirection, tangentY = (float)Math.Cos(angle) * rotationDirection;
            float curve = 4F + (i % 4) * 2.2F;
            PointF start = new PointF(x - tangentX * length / 2F, y - tangentY * length / 2F);
            PointF end = new PointF(x + tangentX * length / 2F, y + tangentY * length / 2F);
            float signedCurve = curve * rotationDirection;
            PointF control1 = new PointF(x - tangentX * length * 0.18F + radialX * signedCurve, y - tangentY * length * 0.18F + radialY * signedCurve);
            PointF control2 = new PointF(x + tangentX * length * 0.18F - radialX * signedCurve, y + tangentY * length * 0.18F - radialY * signedCurve);
            int alpha = 170 + Math.Min(75, keyRate * 5);
            Color colour = windColours[(i + (int)(auraPhase * 5)) % windColours.Length];
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            using (Pen wind = new Pen(Color.FromArgb(alpha, colour), 1.25F + Math.Min(1.15F, keyRate / 24F)))
            {
                path.AddBezier(start, control1, control2, end);
                graphics.DrawPath(wind, path);
            }
        }
    }
    private void DrawOwnedPlanets(Graphics graphics, PointF core)
    {
        int order = 0;
        foreach (string planet in PlanetNames)
        {
            if (!dailyPlanets.Contains(planet)) continue;
            // Radius depends on the Solar-system slot, never on collection order:
            // two owned planets can therefore never share an orbit.
            int slot = PlanetSlot(planet);
            float orbitX = 21F + slot * 4.0F;
            float orbitY = orbitX * 0.39F;
            double angle = auraPhase * (0.22 + slot * 0.013) + slot * 0.87;
            float x = core.X + (float)Math.Cos(angle) * orbitX;
            float y = core.Y + (float)Math.Sin(angle) * orbitY;
            Color colour = PlanetColor(planet);
            float size = PlanetSize(planet);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, colour))) graphics.FillEllipse(glow, x - size - 1.8F, y - size - 1.8F, (size + 1.8F) * 2F, (size + 1.8F) * 2F);
            using (SolidBrush body = new SolidBrush(colour)) graphics.FillEllipse(body, x - size, y - size, size * 2F, size * 2F);
            using (SolidBrush shine = new SolidBrush(Color.FromArgb(185, Color.White))) graphics.FillEllipse(shine, x - size * 0.38F, y - size * 0.42F, Math.Max(1F, size * 0.62F), Math.Max(1F, size * 0.56F));
            order++;
        }
    }
    private static int PlanetSlot(string planet)
    {
        for (int i = 0; i < PlanetNames.Length; i++) if (PlanetNames[i] == planet) return i;
        return 0;
    }
    private static float PlanetSize(string planet)
    {
        if (planet == "Jupiter") return 3.8F;
        if (planet == "Saturn") return 3.35F;
        if (planet == "Neptune" || planet == "Uranus") return 2.75F;
        if (planet == "Earth" || planet == "Venus") return 2.55F;
        if (planet == "Mars") return 2.15F;
        return 1.75F;
    }
    private static Color PlanetColor(string planet)
    {
        switch (planet)
        {
            case "Mercury": return Color.FromArgb(188, 188, 190);
            case "Venus": return Color.FromArgb(235, 190, 112);
            case "Earth": return Color.FromArgb(75, 155, 255);
            case "Mars": return Color.FromArgb(225, 97, 68);
            case "Jupiter": return Color.FromArgb(220, 168, 112);
            case "Saturn": return Color.FromArgb(235, 210, 145);
            case "Uranus": return Color.FromArgb(108, 220, 225);
            case "Neptune": return Color.FromArgb(76, 115, 245);
            default: return Color.White;
        }
    }
    private void DrawAuraParticle(Graphics graphics, AuraParticle particle, Color styleTint, Color coreTint)
    {
        double life = Math.Max(0, particle.Life) / (double)particle.MaxLife;
        int alpha = (int)(40 + life * 180);
        // Colour is sampled once per particle and is intentionally unrelated
        // to its 25x–100x motion tier.
        Color colour = particle.AuraColor;
        if (DateTime.UtcNow < auraDecayRedUntil && particle.DecayRedTarget)
            colour = DecayRedVariant(particle.AuraColor);
        else if (DateTime.UtcNow < auraRewindGreenUntil && particle.RewindGreenTarget)
            colour = RewindAuraColor;
        double settle = GetParticleSettleProgress(particle);
        float scale = 1F - (float)settle * 0.72F;
        if (settle >= 0.995)
        {
            // Final resting dot: exactly one tenth of the former 1.1px diameter.
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, colour))) graphics.FillEllipse(brush, particle.X - 0.055F, particle.Y - 0.055F, 0.11F, 0.11F);
            return;
        }
        // All Aura particles are circular dots; themes only influence motion
        // and ambient colour, never the particle's geometry.
        float diameter = 4.8F * scale;
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, colour)))
            graphics.FillEllipse(brush, particle.X - diameter / 2F, particle.Y - diameter / 2F, diameter, diameter);
    }
    private static void DrawAuraStar(Graphics graphics, float x, float y, float radius, Color colour)
    {
        using (Pen pen = new Pen(colour, 1.15F))
        {
            graphics.DrawLine(pen, x - radius, y, x + radius, y);
            graphics.DrawLine(pen, x, y - radius, x, y + radius);
            graphics.DrawLine(pen, x - radius * 0.58F, y - radius * 0.58F, x + radius * 0.58F, y + radius * 0.58F);
            graphics.DrawLine(pen, x - radius * 0.58F, y + radius * 0.58F, x + radius * 0.58F, y - radius * 0.58F);
        }
    }
    private static Color DecayRedVariant(Color original)
    {
        // White/blue brightness is preserved as white/red brightness.  A pale
        // blue particle becomes pale red, while a deep blue one becomes deep
        // red; decay never flattens the whole Aura into one solid colour.
        double blueStrength = 1.0 - (original.R + original.G) / 510.0;
        blueStrength = Math.Max(0.0, Math.Min(1.0, blueStrength));
        return Blend(Color.FromArgb(255, 248, 248), Color.FromArgb(165, 18, 14), blueStrength);
    }
    private static Color Blend(Color first, Color second, double secondWeight)
    {
        double firstWeight = 1.0 - secondWeight;
        return Color.FromArgb((int)(first.R * firstWeight + second.R * secondWeight), (int)(first.G * firstWeight + second.G * secondWeight), (int)(first.B * firstWeight + second.B * secondWeight));
    }
    private sealed class Particle
    {
        internal float X, Y, VelocityX, VelocityY, Life, MaxLife;
        internal bool IsStar, IsDecayShard, IsSpecial, IsCritical;
        internal Color Color;
    }
    private sealed class AuraParticle
    {
        internal float X, Y, PreviousX, PreviousY, OrbitRadius, BaseOrbitRadius, RadialOffset, RadialVelocity;
        internal double OrbitAngle, OrbitTilt;
        internal int Life, MaxLife, Variant, OrbitDirection;
        internal DateTime CreatedAt;
        internal int SettleDurationSeconds;
        internal int SpeedTier;
        internal Color AuraColor;
        internal bool Entering, DecayRedTarget, RewindGreenTarget;
    }
    private sealed class RareBubbleMessage
    {
        internal string Text;
        internal Color ColorA;
        internal Color ColorB;
    }
    private enum ElementStyle
    {
        Fire = 1, Water, Wind, Earth, Lightning, Ice, Nature, Light, Shadow, Cosmic
    }
    private sealed class AuraDiary
    {
        internal int LastAutoElement = (int)ElementStyle.Light;
        internal int SessionCount;
        internal int LastSessionPeak;
        internal long LastSessionInputs;
        internal int LastSessionCriticals;
        internal long LastSessionTotal;
        internal int BestBurst;
        internal int PeakKeysPerSecond;
        internal readonly HashSet<string> ActiveDays = new HashSet<string>();
    }
    private sealed class FloatingEffect
    {
        internal Label Label;
        internal float X, Y, VelocityX, VelocityY, Life, MaxLife;
        internal bool Increasing;
        internal bool Critical, Burst, Rewind, EchoCascade, FrontControl;
        internal int Multiplier;
        internal Color CriticalColor;
    }
    private class EffectLabel : Label
    {
        internal EffectLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            BorderStyle = BorderStyle.None;
            UseCompatibleTextRendering = false;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { TextRenderer.DrawText(e.Graphics, Text, Font, Point.Empty, ForeColor, TextFormatFlags.NoPadding); }
    }
    private sealed class HudTotalLabel : Label
    {
        internal HudTotalLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; BorderStyle = BorderStyle.None;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
    private sealed class AuraWindow : Form
    {
        private const int WM_NCHITTEST = 0x0084, WM_RBUTTONDOWN = 0x0204, HTCAPTION = 2, HTTRANSPARENT = -1;
        private readonly InputCounterForm owner;
        internal AuraWindow(InputCounterForm ownerForm)
        {
            owner = ownerForm;
            Text = "Input Counter Aura"; TopMost = true; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(AuraWindowSize, AuraWindowSize);
            BackColor = Color.Black; TransparencyKey = Color.Black; DoubleBuffered = true;
            Paint += delegate(object sender, PaintEventArgs e) { owner.DrawTypingAura(e.Graphics); };
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                Point point = PointToClient(Control.MousePosition);
                PointF core = owner.AuraCoreCenter();
                float dx = point.X - core.X, dy = point.Y - core.Y;
                m.Result = dx * dx + dy * dy <= 42F * 42F ? (IntPtr)HTCAPTION : (IntPtr)HTTRANSPARENT;
                return;
            }
            if (m.Msg == WM_RBUTTONDOWN) { owner.Close(); return; }
            base.WndProc(ref m);
        }
    }
    private sealed class DecayShardLabel : EffectLabel
    {
        internal DecayShardLabel(Color colour, int size)
        {
            Text = String.Empty; AutoSize = false; Size = new Size(size, size);
            ForeColor = colour; BackColor = Color.Transparent;
            System.Drawing.Drawing2D.GraphicsPath diamond = new System.Drawing.Drawing2D.GraphicsPath();
            diamond.AddPolygon(new[] { new Point(size / 2, 0), new Point(size - 1, size / 2), new Point(size / 2, size - 1), new Point(0, size / 2) });
            Region = new Region(diamond); diamond.Dispose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float half = Width / 2F;
            PointF[] points = { new PointF(half, 1F), new PointF(Width - 1F, half), new PointF(half, Height - 1F), new PointF(1F, half) };
            using (SolidBrush brush = new SolidBrush(ForeColor)) e.Graphics.FillPolygon(brush, points);
            using (Pen edge = new Pen(Color.FromArgb(225, 255, 190, 170), 0.8F)) e.Graphics.DrawPolygon(edge, points);
        }
    }
    private sealed class CriticalLabel : EffectLabel
    {
        internal CriticalLabel() { BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, Point.Empty, ForeColor);
        }
    }
    private int LoadHighScore()
    {
        try { return int.Parse(System.IO.File.ReadAllText(highScorePath)); }
        catch { return 0; }
    }
    private void SaveHighScore()
    {
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(highScorePath)); System.IO.File.WriteAllText(highScorePath, highScore.ToString()); }
        catch { }
    }
    private long LoadTotalCount()
    {
        try
        {
            string[] record = System.IO.File.ReadAllText(totalPath).Split('|');
            DateTime savedDate;
            long savedTotal;
            if (record.Length == 2 && DateTime.TryParseExact(record[0], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out savedDate) && long.TryParse(record[1], out savedTotal))
            {
                if (savedDate.Date == DateTime.Today) { totalDate = savedDate.Date; return savedTotal; }
                totalDirty = true;
            }
        }
        catch { }
        totalDate = DateTime.Today;
        return 0;
    }
    private void SaveTotalCount()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(totalPath));
            System.IO.File.WriteAllText(totalPath, totalDate.ToString("yyyy-MM-dd") + "|" + totalCount);
            totalDirty = false;
        }
        catch { }
    }
    private bool EnsureDailyTotal()
    {
        DateTime today = DateTime.Today;
        if (totalDate == today) return false;
        long before = totalCount;
        totalDate = today;
        totalCount = 0;
        lastInputCheckpoint = 0;
        rewindGreenActive = false;
        rewindCooldownActive = false;
        rewindDecayDebt = 0;
        totalDirty = true;
        SyncAuraParticlesToScore();
        LogChange("DAILY_RESET", -before, before, 0, recentInputs.Count, 0, null);
        return true;
    }
    private long LoadTotalHighScore()
    {
        try { return long.Parse(System.IO.File.ReadAllText(totalHighScorePath)); }
        catch { return 0; }
    }
    private void SaveTotalHighScore()
    {
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(totalHighScorePath)); System.IO.File.WriteAllText(totalHighScorePath, totalHighScore.ToString()); totalHighScoreDirty = false; }
        catch { }
    }
    private long LoadAllTimeTotal()
    {
        try { return long.Parse(System.IO.File.ReadAllText(allTimePath)); }
        catch { return 0; }
    }
    private void SaveAllTimeTotal()
    {
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(allTimePath)); System.IO.File.WriteAllText(allTimePath, allTimeTotal.ToString()); allTimeDirty = false; }
        catch { }
    }
    private AuraDiary LoadAuraDiary()
    {
        AuraDiary diary = new AuraDiary();
        try
        {
            string[] data = System.IO.File.ReadAllText(auraDiaryPath).Split('|');
            if (data.Length < 9) return diary;
            int.TryParse(data[0], out diary.LastAutoElement);
            int.TryParse(data[1], out diary.SessionCount);
            int.TryParse(data[2], out diary.LastSessionPeak);
            long.TryParse(data[3], out diary.LastSessionInputs);
            int.TryParse(data[4], out diary.LastSessionCriticals);
            long.TryParse(data[5], out diary.LastSessionTotal);
            int.TryParse(data[6], out diary.BestBurst);
            int.TryParse(data[7], out diary.PeakKeysPerSecond);
            foreach (string day in data[8].Split(',')) if (day.Length == 10) diary.ActiveDays.Add(day);
        }
        catch { }
        return diary;
    }
    private void SaveAuraDiary()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(auraDiaryPath));
            string data = auraDiary.LastAutoElement + "|" + auraDiary.SessionCount + "|" + auraDiary.LastSessionPeak + "|" + auraDiary.LastSessionInputs + "|" + auraDiary.LastSessionCriticals + "|" + auraDiary.LastSessionTotal + "|" + auraDiary.BestBurst + "|" + auraDiary.PeakKeysPerSecond + "|" + string.Join(",", auraDiary.ActiveDays);
            System.IO.File.WriteAllText(auraDiaryPath, data);
            auraDiaryDirty = false;
        }
        catch { }
    }
    private void CommitAuraDiary()
    {
        auraDiary.LastSessionPeak = sessionPeakRate;
        auraDiary.LastSessionInputs = sessionInputCount;
        auraDiary.LastSessionCriticals = sessionCriticalCount;
        auraDiary.LastSessionTotal = totalCount;
        auraDiaryDirty = true;
        SaveAuraDiary();
    }
    private void MoveToBottomLeft()
    {
        Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(workArea.Left + 20, workArea.Bottom - Height - 20);
        restingLocation = Location;
    }
    private void FitTotalFont()
    {
        float size = 20F;
        while (size > 5F)
        {
            using (Font probe = new Font("Segoe UI Semibold", size))
                if (TextRenderer.MeasureText(totalLabel.Text, probe).Width <= totalLabel.Width) break;
            size -= 1F;
        }
        if (Math.Abs(totalLabel.Font.Size - size) > 0.1F)
        {
            Font oldFont = totalLabel.Font;
            totalLabel.Font = new Font("Segoe UI Semibold", size);
            oldFont.Dispose();
        }
    }
    private void WindowMouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) Close(); else if (e.Button == MouseButtons.Left) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST)
        {
            Point point = PointToClient(Control.MousePosition);
            if (!hudBackgroundPanel.Bounds.Contains(point)) m.Result = (IntPtr)HTTRANSPARENT;
        }
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { trayIcon.Visible = false; trayIcon.Dispose(); if (auraWindow != null && !auraWindow.IsDisposed) auraWindow.Close(); EnsureDailyTotal(); EnsurePlanetCollectionDate(); SavePlanetCollection(); if (allTimeDirty) SaveAllTimeTotal(); if (totalDirty) SaveTotalCount(); if (totalHighScoreDirty) SaveTotalHighScore(); CommitAuraDiary(); FlushLogs(); shakeTimer.Stop(); totalAnimationTimer.Stop(); auraTimer.Stop(); bubbleTimer.Stop(); if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook); if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook); base.OnFormClosed(e); }
    [STAThread] public static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new InputCounterForm()); }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
