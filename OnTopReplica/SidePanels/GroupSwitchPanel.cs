﻿using System;
using System.Drawing;
using System.Windows.Forms;
using OnTopReplica.Properties;
using System.Collections.Generic;
using OnTopReplica.MessagePumpProcessors;

namespace OnTopReplica.SidePanels {
	partial class GroupSwitchPanel : SidePanel {

        public GroupSwitchPanel() {
			InitializeComponent();
		}

        public override void OnFirstShown(MainForm form) {
            base.OnFirstShown(form);

            LoadWindowList();

            labelStatus.Text = (ParentForm.MessagePumpManager.Get<GroupSwitchManager>().IsActive) ?
                Strings.GroupSwitchModeStatusEnabled :
                Strings.GroupSwitchModeStatusDisabled;
        }

        private void LoadWindowList() {
            var manager = new WindowManager();
            manager.Refresh(WindowManager.EnumerationMode.TaskWindows);

            var imageList = new ImageList();
            foreach (var w in manager.Windows) {
                var item = new ListViewItem(w.Title) {
                    Tag = w
                };

                if (w.Icon != null) {
                    imageList.Images.Add(w.Icon);
                    item.ImageIndex = imageList.Images.Count - 1;
                }

                listWindows.Items.Add(item);
            }
            listWindows.SmallImageList = imageList;
        }

        public override void OnClosing(MainForm form) {
            base.OnClosing(form);

            if (_enableOnClose) {
                List<WindowHandle> ret = new List<WindowHandle>();
                foreach (ListViewItem i in listWindows.SelectedItems) {
                    ret.Add((WindowHandle)i.Tag);
                }
                form.SetThumbnailGroup(ret);
            }
            else {
                form.MessagePumpManager.Get<GroupSwitchManager>().Disable();
            }
        }

        bool _enableOnClose = false;

        private void Enable_click(object sender, EventArgs e) {
            _enableOnClose = true;
            OnRequestClosing();
        }

        private void Cancel_click(object sender, EventArgs e) {
            OnRequestClosing();
        }

	}
}