﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using OnTopReplica.Properties;
using VistaControls.Dwm;
using VistaControls.TaskDialog;
using System.Collections.Generic;
using OnTopReplica.Native;
using OnTopReplica.Update;
using OnTopReplica.StartupOptions;
using OnTopReplica.WindowSeekers;

namespace OnTopReplica {

    partial class MainForm : AspectRatioForm {

        //GUI elements
        ThumbnailPanel _thumbnailPanel;
        SidePanel _currentSidePanel = null;
        Panel _sidePanelContainer;

        //Managers
        BaseWindowSeeker _windowSeeker = new TaskWindowSeeker {
            SkipNotVisibleWindows = true
        };
        MessagePumpManager _msgPumpManager = new MessagePumpManager();
        UpdateManager _updateManager = new UpdateManager();

        Options _startupOptions;

        public MainForm(Options startupOptions) {
            _startupOptions = startupOptions;
            
            //WinForms init pass
            InitializeComponent();
            KeepAspectRatio = false;
            GlassEnabled = true;

            //Store default values
            _nonClickThroughKey = TransparencyKey;

            //Thumbnail panel
            _thumbnailPanel = new ThumbnailPanel {
                Location = Point.Empty,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = ClientSize
            };
            _thumbnailPanel.CloneClick += new EventHandler<CloneClickEventArgs>(Thumbnail_CloneClick);
            Controls.Add(_thumbnailPanel);

            //Side panel
            _sidePanelContainer = new Panel {
                Location = new Point(ClientSize.Width, 0),
                Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
                Enabled = false,
                Visible = false,
                Size = new Size(100, ClientSize.Height),
                Padding = new Padding(4)
            };
            Controls.Add(_sidePanelContainer);

            //Set native renderer on context menus
            Asztal.Szótár.NativeToolStripRenderer.SetToolStripRenderer(
                menuContext, menuWindows, menuOpacity, menuResize, menuLanguages, menuFullscreenContext
            );

            //Init message pump extensions
            _msgPumpManager.Initialize(this);

            //Add hotkeys
            var hotKeyMgr = _msgPumpManager.Get<MessagePumpProcessors.HotKeyManager>();
            hotKeyMgr.RegisterHotKey(Native.HotKeyModifiers.Control | Native.HotKeyModifiers.Shift,
                                     Keys.O, new Native.HotKeyMethods.HotKeyHandler(HotKeyOpenHandler));
            hotKeyMgr.RegisterHotKey(Native.HotKeyModifiers.Control | Native.HotKeyModifiers.Shift,
                                     Keys.C, new Native.HotKeyMethods.HotKeyHandler(HotKeyCloneHandler));

            //Set to Key event preview
            this.KeyPreview = true;
        }

        delegate void GuiAction();

        void UpdateManager_CheckCompleted(object sender, UpdateCheckCompletedEventArgs e) {
            this.Invoke(new GuiAction(() => {
                if (e.Success) {
                    _updateManager.HandleUpdateCheck(this, e.Information, false);
                }
                else {
                    Console.WriteLine("Failed to check for updates: {0}", e.Error);
                }
            }));
        }

        #region Event override

        protected override CreateParams CreateParams {
            get {
                //Needed to hide caption, while keeping window title in task bar
                var parms = base.CreateParams;
                parms.Style &= ~0x00C00000; //WS_CAPTION
                parms.Style &= 0x00040000; //WS_SIZEBOX
                return parms;
            }
        }

        protected override void OnHandleCreated(EventArgs e){
 	        base.OnHandleCreated(e);

            _windowSeeker.OwnerHandle = this.Handle;

            //Platform specific form initialization
            Program.Platform.InitForm(this);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);

            //Apply startup options
            _startupOptions.Apply(this);

            //Check for updates
            _updateManager.UpdateCheckCompleted += new EventHandler<UpdateCheckCompletedEventArgs>(UpdateManager_CheckCompleted);
            _updateManager.CheckForUpdate();
        }

        protected override void OnClosing(CancelEventArgs e) {
            _msgPumpManager.Dispose();

            base.OnClosing(e);
        }

        Margins fullMargins = new Margins(-1);

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);

            this.GlassMargins = (_currentSidePanel != null) ?
                new Margins(ClientSize.Width - _sidePanelContainer.Width, 0, 0, 0) :
                fullMargins;
        }

        protected override void OnResizeEnd(EventArgs e) {
            base.OnResizeEnd(e);

            //If locked in position, move accordingly
            if (PositionLock.HasValue) {
                this.SetScreenPosition(PositionLock.Value);
            }
        }

        protected override void OnActivated(EventArgs e) {
            base.OnActivated(e);

            //Deactivate click-through if form is reactivated
            if (ClickThroughEnabled) {
                ClickThroughEnabled = false;
            }

            Program.Platform.RestoreForm(this);
        }

        protected override void OnDeactivate(EventArgs e) {
            base.OnDeactivate(e);

            //HACK: sometimes, even if TopMost is true, the window loses its "always on top" status.
            //  This is a fix attempt that probably won't work...
            if (!IsFullscreen) { //fullscreen mode doesn't use TopMost
                TopMost = false;
                TopMost = true;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);

            if (!IsFullscreen) {
                int change = (int)(e.Delta / 6.0); //assumes a mouse wheel "tick" is in the 80-120 range
                AdjustSize(change);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e) {
            base.OnMouseDoubleClick(e);

            //This is handled by the WM_NCLBUTTONDBLCLK msg handler usually (because the GlassForm translates
            //clicks on client to clicks on caption). But if fullscreen mode disables GlassForm dragging, we need
            //this auxiliary handler to switch mode.
            IsFullscreen = !IsFullscreen;
        }

        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e);

            //Same story as above (OnMouseDoubleClick)
            if (e.Button == System.Windows.Forms.MouseButtons.Right) {
                OpenContextMenu();
            }
        }

        protected override void WndProc(ref Message m) {
            if (_msgPumpManager != null) {
                if (_msgPumpManager.PumpMessage(ref m)) {
                    return;
                }
            }

            switch (m.Msg) {
                case WM.NCRBUTTONUP:
                    //Open context menu if right button clicked on caption (i.e. all of the window area because of glass)
                    if (m.WParam.ToInt32() == HT.CAPTION) {
                        OpenContextMenu();

                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case WM.NCLBUTTONDBLCLK:
                    //Toggle fullscreen mode if double click on caption (whole glass area)
                    if (m.WParam.ToInt32() == HT.CAPTION) {
                        IsFullscreen = !IsFullscreen;

                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case WM.NCHITTEST:
                    //Make transparent to hit-testing if in click through mode
                    if (ClickThroughEnabled && (ModifierKeys & Keys.Alt) != Keys.Alt) {
                        m.Result = (IntPtr)HT.TRANSPARENT;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        #endregion

        const string Title = "OnTopReplica";

        #region Keyboard event handling

        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);

            //ALT
            if (e.Modifiers == Keys.Alt) {
                if (e.KeyCode == Keys.Enter) {
                    e.Handled = true;
                    IsFullscreen = !IsFullscreen;
                }

                else if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) {
                    FitToThumbnail(0.25);
                }

                else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) {
                    FitToThumbnail(0.5);
                }

                else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3 || e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) {
                    FitToThumbnail(1.0);
                }

                else if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) {
                    FitToThumbnail(2.0);
                }
            }

            //ESCAPE
            else if (e.KeyCode == Keys.Escape) {

#if DEBUG
                Console.WriteLine("Received ESCAPE");
#endif

                //Disable click-through
                if (ClickThroughEnabled) {
                    ClickThroughEnabled = false;
                }
                //Toggle fullscreen
                else if (IsFullscreen) {
                    IsFullscreen = false;
                }
                //Disable click forwarding
                else if (ClickForwardingEnabled) {
                    ClickForwardingEnabled = false;
                }
            }
        }

        void HotKeyOpenHandler() {
            if (IsFullscreen)
                IsFullscreen = false;

            if (!Program.Platform.IsHidden(this)) {
                Program.Platform.HideForm(this);
            }
            else {
                EnsureMainFormVisible();
            }
        }

        void HotKeyCloneHandler() {
            var handle = Win32Helper.GetCurrentForegroundWindow();
            if (handle.Handle == this.Handle)
                return;

            SetThumbnail(handle, null);
        }

        #endregion

        #region Fullscreen

        bool _isFullscreen = false;
        Point _preFullscreenLocation;
        Size _preFullscreenSize;
        FormBorderStyle _preFullscreenBorderStyle;

        public bool IsFullscreen {
            get {
                return _isFullscreen;
            }
            set {
                if (IsFullscreen == value)
                    return;
                if (value && !_thumbnailPanel.IsShowingThumbnail)
                    return;

                CloseSidePanel(); //on switch, always hide side panels

                //Location and size
                if (value) {
                    _preFullscreenLocation = Location;
                    _preFullscreenSize = ClientSize;
                    _preFullscreenBorderStyle = FormBorderStyle;

                    var currentScreen = Screen.FromControl(this);
                    FormBorderStyle = FormBorderStyle.None;
                    Size = currentScreen.WorkingArea.Size;
                    Location = currentScreen.WorkingArea.Location;
                }
                else {
                    FormBorderStyle = _preFullscreenBorderStyle;
                    Location = _preFullscreenLocation;
                    ClientSize = _preFullscreenSize;
                    RefreshAspectRatio();
                }

                //Common
                GlassEnabled = !value;
                TopMost = !value;
                HandleMouseMove = !value;

                _isFullscreen = value;

                Program.Platform.OnFormStateChange(this);
            }
        }

        #endregion

        #region Thumbnail operation

        /// <summary>
        /// Sets a new thumbnail.
        /// </summary>
        /// <param name="handle">Handle to the window to clone.</param>
        /// <param name="region">Region of the window to clone.</param>
        public void SetThumbnail(WindowHandle handle, Rectangle? region) {
            try {
                CurrentThumbnailWindowHandle = handle;
                _thumbnailPanel.SetThumbnailHandle(handle);

#if DEBUG
                string windowClass = WindowMethods.GetWindowClass(handle.Handle);
                Console.WriteLine("Cloning window HWND {0} of class {1}.", handle.Handle, windowClass);
#endif

                if (region.HasValue)
                    _thumbnailPanel.SelectedRegion = region.Value;
                else
                    _thumbnailPanel.ConstrainToRegion = false;

                //Set aspect ratio (this will resize the form), do not refresh if in fullscreen
                SetAspectRatio(_thumbnailPanel.ThumbnailOriginalSize, !IsFullscreen);
            }
            catch (Exception ex) {
                ThumbnailError(ex, false, Strings.ErrorUnableToCreateThumbnail);
            }
        }

        /// <summary>
        /// Enables group mode on a list of window handles.
        /// </summary>
        /// <param name="handles">List of window handles.</param>
        public void SetThumbnailGroup(IList<WindowHandle> handles) {
            if (handles.Count == 0)
                return;

            //At last one thumbnail
            SetThumbnail(handles[0], null);

            //Handle if no real group
            if (handles.Count == 1)
                return;

            CurrentThumbnailWindowHandle = null;
            _msgPumpManager.Get<MessagePumpProcessors.GroupSwitchManager>().EnableGroupMode(handles);
        }

        /// <summary>
        /// Disables the cloned thumbnail.
        /// </summary>
        public void UnsetThumbnail() {
            //Unset handle
            CurrentThumbnailWindowHandle = null;
            _thumbnailPanel.UnsetThumbnail();

            //Disable aspect ratio
            KeepAspectRatio = false;
        }

        /// <summary>
        /// Gets or sets the region displayed of the current thumbnail.
        /// </summary>
        public Rectangle? SelectedThumbnailRegion {
            get {
                if (!_thumbnailPanel.IsShowingThumbnail || !_thumbnailPanel.ConstrainToRegion)
                    return null;

                return _thumbnailPanel.SelectedRegion;
            }
            set {
                if (!_thumbnailPanel.IsShowingThumbnail)
                    return;

                if (value.HasValue) {
                    _thumbnailPanel.SelectedRegion = value.Value;
                    SetAspectRatio(value.Value.Size, true);
                }
                else {
                    _thumbnailPanel.ConstrainToRegion = false;
                    SetAspectRatio(_thumbnailPanel.ThumbnailOriginalSize, true);
                }

                FixPositionAndSize();
            }
        }

        const int FixMargin = 10;

        /// <summary>
        /// Fixes the form's position and size, ensuring it is fully displayed in the current screen.
        /// </summary>
        private void FixPositionAndSize() {
            var screen = Screen.FromControl(this);

            if (Width > screen.WorkingArea.Width) {
                Width = screen.WorkingArea.Width - FixMargin;
            }
            if (Height > screen.WorkingArea.Height) {
                Height = screen.WorkingArea.Height - FixMargin;
            }
            if (Location.X + Width > screen.WorkingArea.Right) {
                Location = new Point(screen.WorkingArea.Right - Width - FixMargin, Location.Y);
            }
            if (Location.Y + Height > screen.WorkingArea.Bottom) {
                Location = new Point(Location.X, screen.WorkingArea.Bottom - Height - FixMargin);
            }
        }

        private void ThumbnailError(Exception ex, bool suppress, string title) {
            if (!suppress) {
                ShowErrorDialog(title, Strings.ErrorGenericThumbnailHandleError, ex.Message);
            }

            UnsetThumbnail();
        }

        /// <summary>Automatically sizes the window in order to accomodate the thumbnail p times.</summary>
        /// <param name="p">Scale of the thumbnail to consider.</param>
        private void FitToThumbnail(double p) {
            try {
                Size originalSize = _thumbnailPanel.ThumbnailOriginalSize;
                Size fittedSize = new Size((int)(originalSize.Width * p), (int)(originalSize.Height * p));
                ClientSize = fittedSize;
            }
            catch (Exception ex) {
                ThumbnailError(ex, false, Strings.ErrorUnableToFit);
            }
        }

        #endregion

        #region Accessors

        /// <summary>
        /// Gets the form's thumbnail panel.
        /// </summary>
        public ThumbnailPanel ThumbnailPanel {
            get {
                return _thumbnailPanel;
            }
        }

        /// <summary>
        /// Gets the form's message pump manager.
        /// </summary>
        public MessagePumpManager MessagePumpManager {
            get {
                return _msgPumpManager;
            }
        }

        /// <summary>
        /// Gets the form's window list drop down menu.
        /// </summary>
        public ContextMenuStrip MenuWindows {
            get {
                return menuWindows;
            }
        }

        /// <summary>
        /// Retrieves the window handle of the currently cloned thumbnail.
        /// </summary>
        public WindowHandle CurrentThumbnailWindowHandle {
            get;
            private set;
        }

        #endregion
        
    }
}
