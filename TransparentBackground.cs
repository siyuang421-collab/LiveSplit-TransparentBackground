using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

[assembly: LiveSplit.UI.Components.ComponentFactory(typeof(LiveSplit.TransparentBackground.TransparentBackgroundFactory))]

namespace LiveSplit.TransparentBackground
{
    /// <summary>
    /// LiveSplit 组件工厂。LiveSplit 启动时会扫描 Components 目录并实例化所有工厂，
    /// 构造函数里就把后台线程拉起来，因此不需要把组件加进布局也能生效。
    /// </summary>
    public class TransparentBackgroundFactory : IComponentFactory
    {
        public TransparentBackgroundFactory()
        {
            TransparencyAgent.Start();
        }

        public string ComponentName
        {
            get { return "透明背景"; }
        }

        public string Description
        {
            get { return "把计时器窗口的背景色设为透明（颜色键），不改布局文件。"; }
        }

        public ComponentCategory Category
        {
            get { return ComponentCategory.Other; }
        }

        public string UpdateName
        {
            get { return ComponentName; }
        }

        public string XMLURL
        {
            get { return ""; }
        }

        public string UpdateURL
        {
            get { return ""; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0, 0); }
        }

        public IComponent Create(LiveSplitState state)
        {
            return new TransparentBackgroundComponent(state);
        }
    }

    /// <summary>
    /// 占位组件：不做任何绘制，真正的工作在 TransparencyAgent 里。
    /// </summary>
    public class TransparentBackgroundComponent : IComponent
    {
        private readonly LiveSplitState _state;

        public TransparentBackgroundComponent(LiveSplitState state)
        {
            _state = state;
        }

        public string ComponentName
        {
            get { return "透明背景"; }
        }

        public float HorizontalWidth { get { return 0f; } }
        public float MinimumWidth { get { return 0f; } }
        public float VerticalHeight { get { return 0f; } }
        public float MinimumHeight { get { return 0f; } }
        public float PaddingTop { get { return 0f; } }
        public float PaddingBottom { get { return 0f; } }
        public float PaddingLeft { get { return 0f; } }
        public float PaddingRight { get { return 0f; } }

        public IDictionary<string, Action> ContextMenuControls
        {
            get { return new Dictionary<string, Action>(); }
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clip)
        {
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clip)
        {
        }

        public Control GetSettingsControl(LayoutMode mode)
        {
            return null;
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            return document.CreateElement("TransparentBackground");
        }

        public void SetSettings(XmlNode settings)
        {
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
        }

        public void Dispose()
        {
        }
    }

    internal static class TransparencyAgent
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint LWA_COLORKEY = 0x00000001;
        private const uint LWA_ALPHA = 0x00000002;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private static readonly object Sync = new object();
        private static readonly Color KeyColor = Color.FromArgb(255, 1, 2, 3);
        private static readonly Color SplitTextColor = Color.White;
        private static bool _started;
        private static IntPtr _lastHandle = IntPtr.Zero;
        private static Color _lastKey = Color.Empty;
        private static bool _lastApplied = false;
        private static int _lastStyle = 0;
        private static int _lastRegionWidth = 0;
        private static int _lastRegionHeight = 0;
        private static Form _proxy;
        private static Form _proxyTimer;
        private static bool _proxyPending;
        private static DateTime _proxyResumeAt = DateTime.MinValue;
        private static readonly HashSet<object> _splitsSettingsLogged = new HashSet<object>();
        private static readonly HashSet<object> _layoutSettingsLogged = new HashSet<object>();

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "LiveSplitTransparent.log");

        private static TextWriter _log;

        public static void Start()
        {
            lock (Sync)
            {
                if (_started)
                {
                    return;
                }
                _started = true;
            }

            try
            {
                _log = new StreamWriter(LogPath, false, Encoding.UTF8);
                Log("agent start");
            }
            catch
            {
                _log = null;
            }

            Thread thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "LiveSplitTransparent";
            thread.Start();
        }

        private static void Log(string message)
        {
            try
            {
                if (_log == null)
                {
                    return;
                }
                _log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
                _log.Flush();
            }
            catch
            {
            }
        }

        private static void Loop()
        {
            while (true)
            {
                try
                {
                    ApplyOnce();
                }
                catch (Exception e)
                {
                    Log("error: " + e);
                }
                Thread.Sleep(250);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static Form FindTimerForm()
        {
            try
            {
                foreach (Form form in Application.OpenForms)
                {
                    if (form != null && form.GetType().FullName == "LiveSplit.View.TimerForm")
                    {
                        return form;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void PatchSplitsRows(Form form, Color rowColor)
        {
            try
            {
                object layoutObject = form.GetType().GetProperty("Layout").GetValue(form, null);
                if (layoutObject == null)
                {
                    return;
                }
                LiveSplit.UI.ILayout layout = (LiveSplit.UI.ILayout)layoutObject;
                if (layout.LayoutComponents == null)
                {
                    return;
                }
                Color solid = Color.FromArgb(255, rowColor.R, rowColor.G, rowColor.B);
                foreach (LiveSplit.UI.Components.ILayoutComponent layoutComponent in layout.LayoutComponents)
                {
                    if (layoutComponent == null)
                    {
                        continue;
                    }
                    LiveSplit.UI.Components.IComponent component = layoutComponent.Component;
                    if (component == null || component.GetType().FullName != "LiveSplit.UI.Components.SplitsComponent")
                    {
                        continue;
                    }
                    PropertyInfo settingsProperty = component.GetType().GetProperty(
                        "Settings",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (settingsProperty == null)
                    {
                        Log("no Settings property on " + component.GetType().FullName);
                        continue;
                    }
                    object splitsSettings = settingsProperty.GetValue(component, null);
                    if (splitsSettings == null)
                    {
                        continue;
                    }
                    if (_splitsSettingsLogged.Add(splitsSettings))
                    {
                        Log("splits colors bg1=" + GetColor(splitsSettings, "BackgroundColor")
                            + " bg2=" + GetColor(splitsSettings, "BackgroundColor2")
                            + " curTop=" + GetColor(splitsSettings, "CurrentSplitTopColor")
                            + " curBottom=" + GetColor(splitsSettings, "CurrentSplitBottomColor")
                            + " labels=" + GetColor(splitsSettings, "LabelsColor")
                            + " beforeNames=" + GetColor(splitsSettings, "BeforeNamesColor")
                            + " beforeTimes=" + GetColor(splitsSettings, "BeforeTimesColor")
                            + " afterNames=" + GetColor(splitsSettings, "AfterNamesColor")
                            + " afterTimes=" + GetColor(splitsSettings, "AfterTimesColor")
                            + " currentNames=" + GetColor(splitsSettings, "CurrentNamesColor")
                            + " currentTimes=" + GetColor(splitsSettings, "CurrentTimesColor")
                            + " deltas=" + GetColor(splitsSettings, "DeltasColor"));
                    }
                    bool dirty = false;
                    dirty |= SetColor(splitsSettings, "BackgroundColor", solid);
                    dirty |= SetColor(splitsSettings, "BackgroundColor2", solid);
                    dirty |= SetColor(splitsSettings, "CurrentSplitTopColor", solid);
                    dirty |= SetColor(splitsSettings, "CurrentSplitBottomColor", solid);
                    dirty |= SetColor(splitsSettings, "LabelsColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "BeforeNamesColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "BeforeTimesColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "AfterNamesColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "AfterTimesColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "CurrentNamesColor", SplitTextColor);
                    dirty |= SetColor(splitsSettings, "CurrentTimesColor", SplitTextColor);

                    // 每一行实际是 SplitComponent，它可能持有自己的 Settings 副本；
                    // 只改父级 SplitsSettings 不会影响已经创建出来的行。
                    try
                    {
                        PropertyInfo childrenProperty = component.GetType().GetProperty(
                            "SplitComponents",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        object splitComponents = childrenProperty == null ? null : childrenProperty.GetValue(component, null);
                        if (splitComponents is System.Collections.IEnumerable)
                        {
                            int patchedChildren = 0;
                            foreach (object child in (System.Collections.IEnumerable)splitComponents)
                            {
                                if (child == null)
                                {
                                    continue;
                                }
                                PropertyInfo childSettingsProperty = child.GetType().GetProperty(
                                    "Settings",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (childSettingsProperty == null)
                                {
                                    continue;
                                }
                                object childSettings = childSettingsProperty.GetValue(child, null);
                                if (childSettings == null || childSettings == splitsSettings)
                                {
                                    continue;
                                }
                                dirty |= SetColor(childSettings, "BackgroundColor", solid);
                                dirty |= SetColor(childSettings, "BackgroundColor2", solid);
                                dirty |= SetColor(childSettings, "CurrentSplitTopColor", solid);
                                dirty |= SetColor(childSettings, "CurrentSplitBottomColor", solid);
                                dirty |= SetColor(childSettings, "LabelsColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "BeforeNamesColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "BeforeTimesColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "AfterNamesColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "AfterTimesColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "CurrentNamesColor", SplitTextColor);
                                dirty |= SetColor(childSettings, "CurrentTimesColor", SplitTextColor);
                                patchedChildren++;
                            }
                            if (patchedChildren > 0)
                            {
                                Log("split children patched " + patchedChildren);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log("patch split children failed: " + e.Message);
                    }

                    if (dirty)
                    {
                        // 只改 Settings 里的颜色，已有的 SplitComponent 还在用旧颜色绘制；
                        // 必须让它重建一次行组件，否则界面上仍是原来那层深灰底。
                        // 重建会碰控件，放回 UI 线程执行。
                        try
                        {
                            MethodInfo rebuild = component.GetType().GetMethod(
                                "RebuildVisualSplits",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (rebuild != null)
                            {
                                form.BeginInvoke(new Action(delegate
                                {
                                    try
                                    {
                                        rebuild.Invoke(component, null);
                                        ClearSplitShadows(component);
                                        form.Invalidate(true);
                                        Log("rebuild splits done");
                                    }
                                    catch (Exception e)
                                    {
                                        Log("rebuild splits failed: " + e.Message);
                                    }
                                }));
                            }
                        }
                        catch (Exception e)
                        {
                            Log("rebuild splits schedule failed: " + e.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log("patch splits failed: " + e);
            }
        }

        private static Color GetColor(object settings, string name)
        {
            try
            {
                PropertyInfo p = settings.GetType().GetProperty(name);
                if (p == null)
                {
                    return Color.Empty;
                }
                return (Color)p.GetValue(settings, null);
            }
            catch
            {
                return Color.Empty;
            }
        }

        private static bool SetColor(object settings, string name, Color value)
        {
            try
            {
                PropertyInfo p = settings.GetType().GetProperty(name);
                if (p == null || p.PropertyType != typeof(Color))
                {
                    return false;
                }
                Color old = (Color)p.GetValue(settings, null);
                if (old != value)
                {
                    p.SetValue(settings, value, null);
                    Log("splits " + name + " " + old + " -> " + value);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log("set splits " + name + " failed: " + e.Message);
            }
            return false;
        }

        private static void ClearSplitShadows(object splitsComponent)
        {
            try
            {
                PropertyInfo shadowImagesProperty = splitsComponent.GetType().GetProperty(
                    "ShadowImages",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object shadowImages = shadowImagesProperty == null ? null : shadowImagesProperty.GetValue(splitsComponent, null);
                if (shadowImages is System.Collections.IDictionary)
                {
                    ((System.Collections.IDictionary)shadowImages).Clear();
                    Log("cleared split shadow images");
                }

                PropertyInfo splitComponentsProperty = splitsComponent.GetType().GetProperty(
                    "SplitComponents",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object splitComponents = splitComponentsProperty == null ? null : splitComponentsProperty.GetValue(splitsComponent, null);
                if (splitComponents is System.Collections.IEnumerable)
                {
                    foreach (object child in (System.Collections.IEnumerable)splitComponents)
                    {
                        if (child == null)
                        {
                            continue;
                        }
                        PropertyInfo shadowProperty = child.GetType().GetProperty(
                            "ShadowImage",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (shadowProperty != null && shadowProperty.CanWrite)
                        {
                            shadowProperty.SetValue(child, null, null);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log("clear split shadows failed: " + e.Message);
            }
        }

        private static void ApplyOnce()
        {
            Form form = FindTimerForm();
            if (form == null || !form.IsHandleCreated || !form.Visible)
            {
                return;
            }

            QueueProxy(form);

            LiveSplit.Options.LayoutSettings settings = null;
            try
            {
                object layout = form.GetType().GetProperty("Layout").GetValue(form, null);
                if (layout != null)
                {
                    object s = layout.GetType().GetProperty("Settings").GetValue(layout, null);
                    if (s is LiveSplit.Options.LayoutSettings)
                    {
                        settings = (LiveSplit.Options.LayoutSettings)s;
                    }
                }
            }
            catch (Exception e)
            {
                Log("read layout failed: " + e.Message);
            }

            Color key = KeyColor;
            string typeName = "unknown";
            float imageOpacity = 0f;
            if (settings != null)
            {
                typeName = settings.BackgroundType.ToString();
                imageOpacity = settings.ImageOpacity;
                if (_layoutSettingsLogged.Add(settings))
                {
                    Log("layout colors bg=" + settings.BackgroundColor
                        + " bg2=" + settings.BackgroundColor2
                        + " text=" + settings.TextColor
                        + " outline=" + settings.TextOutlineColor
                        + " separators=" + settings.SeparatorsColor
                        + " shadows=" + settings.ShadowsColor
                        + " opacity=" + settings.Opacity);
                }

                // 不再拿布局背景色当颜色键：布局的深灰/黑色经常和文字颜色
                // 或文字描边颜色撞车，一旦相同，文字会被一起抠掉。
                // 改成一个只用于抠图的专用色（接近黑，肉眼不可见），
                // 并强制把布局背景和分段行背景都刷成这个颜色。
                try
                {
                    settings.BackgroundType = LiveSplit.Options.BackgroundType.SolidColor;
                    settings.BackgroundColor = key;
                    settings.BackgroundColor2 = key;
                    settings.ImageOpacity = 0f;
                    settings.DropShadows = false;
                    settings.ShadowsColor = Color.Transparent;
                    settings.TextColor = SplitTextColor;
                    if (settings.Opacity < 1f)
                    {
                        settings.Opacity = 1f;
                    }
                    typeName = settings.BackgroundType.ToString();
                    imageOpacity = 0f;
                }
                catch (Exception e)
                {
                    Log("force background failed: " + e.Message);
                }
            }
            else
            {
                form.BackColor = key;
            }

            try
            {
                form.BackColor = key;
            }
            catch
            {
            }

            // 分段行自己还有一层深灰/渐变底色（默认布局里是 #171717 / #1C1C1C），
            // 单纯抠窗口背景抠不掉。把分段行底色也改成“颜色键”同一个颜色。
            PatchSplitsRows(form, key);

            IntPtr handle = form.Handle;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            bool styleChanged = false;
            if ((style & WS_EX_LAYERED) == 0)
            {
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_LAYERED);
                styleChanged = true;
                Log("added WS_EX_LAYERED hwnd=0x" + handle.ToInt64().ToString("X") + " type=" + typeName);
            }

            // 颜色键会让被抠掉的像素同时鼠标穿透，导致拖不动、右键没反应。
            // 给窗口设一个覆盖整个窗口的矩形区域，强制这些透明像素仍然参与命中测试。
            int regionWidth = form.Width;
            int regionHeight = form.Height;
            if (regionWidth > 0 && regionHeight > 0
                && (handle != _lastHandle || regionWidth != _lastRegionWidth || regionHeight != _lastRegionHeight))
            {
                IntPtr region = CreateRectRgn(0, 0, regionWidth, regionHeight);
                if (region != IntPtr.Zero)
                {
                    int regionResult = SetWindowRgn(handle, region, true);
                    if (regionResult == 0)
                    {
                        DeleteObject(region);
                        Log("SetWindowRgn failed lastError=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    }
                    else
                    {
                        _lastRegionWidth = regionWidth;
                        _lastRegionHeight = regionHeight;
                        Log("set full-window region " + regionWidth + "x" + regionHeight);
                    }
                }
            }

            if (form.Opacity < 1.0)
            {
                try
                {
                    Log("reset form opacity from " + form.Opacity);
                    form.BeginInvoke(new Action(delegate { form.Opacity = 1.0; }));
                }
                catch
                {
                }
            }

            uint colorRef = (uint)((key.B << 16) | (key.G << 8) | key.R);
            bool changed = styleChanged || handle != _lastHandle || key != _lastKey || !_lastApplied;
            if (!changed)
            {
                return;
            }

            bool ok = SetLayeredWindowAttributes(handle, colorRef, 0, LWA_COLORKEY);
            Log("apply key=#" + key.R.ToString("X2") + key.G.ToString("X2") + key.B.ToString("X2")
                + " alpha=" + key.A + " type=" + typeName + " imageOpacity=" + imageOpacity
                + " ok=" + ok + " lastError=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            _lastHandle = handle;
            _lastKey = key;
            _lastStyle = style | WS_EX_LAYERED;
            _lastApplied = ok;
        }

        private static void QueueProxy(Form timer)
        {
            if (_proxyPending)
            {
                return;
            }
            _proxyPending = true;
            try
            {
                timer.BeginInvoke(new Action(delegate
                {
                    _proxyPending = false;
                    try
                    {
                        EnsureProxy(timer);
                    }
                    catch (Exception e)
                    {
                        Log("proxy error: " + e.Message);
                    }
                }));
            }
            catch
            {
                _proxyPending = false;
            }
        }

        private static bool IsMenuOpen()
        {
            uint pid = GetCurrentProcessId();
            IntPtr timer = _lastHandle;
            IntPtr proxy = (_proxy != null && !_proxy.IsDisposed) ? _proxy.Handle : IntPtr.Zero;
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == timer || h == proxy)
                {
                    return true;
                }
                if (!IsWindowVisible(h))
                {
                    return true;
                }
                uint windowPid;
                GetWindowThreadProcessId(h, out windowPid);
                if (windowPid != pid)
                {
                    return true;
                }
                StringBuilder name = new StringBuilder(256);
                GetClassName(h, name, 256);
                if (name.ToString() == "#32768")
                {
                    found = true;
                    return false;
                }

                StringBuilder title = new StringBuilder(256);
                GetWindowText(h, title, 256);
                string text = title.ToString();
                if (text.IndexOf("Edit Splits", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Edit Layout", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static void EnsureProxy(Form timer)
        {
            if (timer == null || timer.IsDisposed || !timer.Visible)
            {
                HideProxy();
                return;
            }

            // LiveSplit 自己开了“跑动时鼠标穿透”的话，代理窗也要跟着让开。
            if ((GetWindowLong(timer.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0)
            {
                HideProxy();
                return;
            }

            if (DateTime.Now < _proxyResumeAt || IsMenuOpen())
            {
                HideProxy();
                return;
            }

            if (_proxy != null && !_proxy.IsDisposed && _proxyTimer == timer)
            {
                Rectangle bounds = timer.Bounds;
                if (_proxy.Bounds != bounds)
                {
                    _proxy.Bounds = bounds;
                }
                if (!_proxy.Visible)
                {
                    _proxy.Show();
                    Log("proxy shown bounds=" + bounds);
                }
                // 其他置顶窗口（聊天、浏览器等）被激活后会压到代理窗上面，
                // 每次同步都把代理窗重新提回最顶层。
                SetWindowPos(_proxy.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                return;
            }

            DisposeProxy();
            _proxyTimer = timer;
            MouseProxyForm proxy = new MouseProxyForm(timer);
            proxy.Bounds = timer.Bounds;
            proxy.Show();
            _proxy = proxy;
            Log("proxy created bounds=" + timer.Bounds);
        }

        private static void HideProxy()
        {
            if (_proxy != null && !_proxy.IsDisposed && _proxy.Visible)
            {
                _proxy.Hide();
                Log("proxy hidden");
            }
        }

        private static void DisposeProxy()
        {
            if (_proxy != null && !_proxy.IsDisposed)
            {
                _proxy.Dispose();
            }
            _proxy = null;
            _proxyTimer = null;
        }

        private static Color SampleBackground(Form form)
        {
            try
            {
                IntPtr dc = GetDC(form.Handle);
                if (dc == IntPtr.Zero)
                {
                    return Color.Empty;
                }
                int w = form.ClientSize.Width;
                int h = form.ClientSize.Height;
                int[] xs = new int[] { 3, 3, w - 4, w - 4, w / 2, 3, w - 4, w / 2 };
                int[] ys = new int[] { 3, h - 4, 3, h - 4, 3, h / 2, h / 2, h - 4 };
                Dictionary<int, int> counts = new Dictionary<int, int>();
                for (int i = 0; i < xs.Length; i++)
                {
                    uint px = GetPixel(dc, xs[i], ys[i]);
                    if (px == 0xFFFFFFFF)
                    {
                        continue;
                    }
                    int c = (int)px;
                    counts[c] = (counts.ContainsKey(c) ? counts[c] : 0) + 1;
                }
                ReleaseDC(form.Handle, dc);
                int best = -1;
                int bestCount = 0;
                foreach (KeyValuePair<int, int> kv in counts)
                {
                    if (kv.Value > bestCount)
                    {
                        best = kv.Key;
                        bestCount = kv.Value;
                    }
                }
                if (best < 0)
                {
                    return Color.Empty;
                }
                return Color.FromArgb(255, best & 0xFF, (best >> 8) & 0xFF, (best >> 16) & 0xFF);
            }
            catch
            {
                return Color.Empty;
            }
        }

        // 颜色键会让透明区域鼠标穿透。这个几乎不可见的小窗口盖在计时器上，
        // 专门接鼠标：左键自己移动计时器，右键直接给计时器发菜单消息。
        private sealed class MouseProxyForm : Form
        {
            private readonly Form _timer;
            private bool _dragging;
            private Point _dragStart;
            private Point _timerStart;

            public MouseProxyForm(Form timer)
            {
                _timer = timer;
                FormBorderStyle = FormBorderStyle.None;
                AutoScaleMode = AutoScaleMode.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.Black;
                Opacity = 0.004;
                MouseDown += OnProxyMouseDown;
                MouseMove += OnProxyMouseMove;
                MouseUp += OnProxyMouseUp;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                    return cp;
                }
            }

            private void OnProxyMouseDown(object sender, MouseEventArgs e)
            {
                try
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        _dragging = true;
                        _dragStart = Cursor.Position;
                        _timerStart = _timer.Location;
                        Capture = true;
                        Log("proxy drag start timer=" + _timerStart);
                    }
                    else if (e.Button == MouseButtons.Right)
                    {
                        ShowTimerMenu();
                    }
                }
                catch (Exception ex)
                {
                    Log("proxy mouse error: " + ex.Message);
                }
            }

            private void OnProxyMouseMove(object sender, MouseEventArgs e)
            {
                if (!_dragging)
                {
                    return;
                }
                Point now = Cursor.Position;
                int dx = now.X - _dragStart.X;
                int dy = now.Y - _dragStart.Y;
                SetWindowPos(_timer.Handle, IntPtr.Zero,
                    _timerStart.X + dx, _timerStart.Y + dy,
                    0, 0, 0x0001 | 0x0004 | 0x0010);
            }

            private void OnProxyMouseUp(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && _dragging)
                {
                    _dragging = false;
                    Capture = false;
                    Log("proxy drag end timer=" + _timer.Location);
                }
            }

            private void ShowTimerMenu()
            {
                _proxyResumeAt = DateTime.Now.AddMilliseconds(800);
                Point screen = Cursor.Position;
                int clientX = screen.X - _timer.Left;
                int clientY = screen.Y - _timer.Top;
                IntPtr client = (IntPtr)((clientY << 16) | (clientX & 0xFFFF));
                IntPtr screenPoint = (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF));

                Hide();
                PostMessage(_timer.Handle, WM_RBUTTONDOWN, (IntPtr)2, client);
                PostMessage(_timer.Handle, WM_RBUTTONUP, IntPtr.Zero, client);
                PostMessage(_timer.Handle, WM_CONTEXTMENU, _timer.Handle, screenPoint);
                Log("proxy menu posted at " + screen.X + "," + screen.Y);
            }
        }
    }
}
