﻿using System.Drawing;
using System.Windows.Forms;
using VistaControls.TaskDialog;

namespace OnTopReplica {
    partial class MainForm {

        /// <summary>
        /// Opens the context menu.
        /// </summary>
        public void OpenContextMenu() {
            if (IsFullscreen) {
                menuFullscreenContext.Show(MousePosition);
            }
            else {
                menuContext.Show(MousePosition);
            }
        }

        private Point RecenterLocation(Control original, Control final) {
            int origX = original.Location.X + original.Size.Width / 2;
            int origY = original.Location.Y + original.Size.Height / 2;

            int finX = origX - final.Size.Width / 2;
            int finY = origY - final.Size.Height / 2;

            //Check boundaries
            var screen = Screen.FromControl(final);
            if (finX < screen.WorkingArea.X)
                finX = screen.WorkingArea.X;
            if (finX + final.Size.Width > screen.WorkingArea.Width)
                finX = screen.WorkingArea.Width - final.Size.Width;
            if (finY < screen.WorkingArea.Y)
                finY = screen.WorkingArea.Y;
            if (finY + final.Size.Height > screen.WorkingArea.Height)
                finY = screen.WorkingArea.Height - final.Size.Height;

            return new Point(finX, finY);
        }

        private int ChromeBorderVertical {
            get {
                if (FormBorderStyle == FormBorderStyle.Sizable)
                    return SystemInformation.FrameBorderSize.Height;
                else
                    return 0;
            }
        }

        private int ChromeBorderHorizontal {
            get {
                if (FormBorderStyle == FormBorderStyle.Sizable)
                    return SystemInformation.FrameBorderSize.Width;
                else
                    return 0;
            }
        }

        /// <summary>
        /// Displays an error task dialog.
        /// </summary>
        /// <param name="mainInstruction">Main instruction of the error dialog.</param>
        /// <param name="explanation">Detailed informations about the error.</param>
        /// <param name="errorMessage">Expanded error codes/messages.</param>
        private void ShowErrorDialog(string mainInstruction, string explanation, string errorMessage) {
            TaskDialog dlg = new TaskDialog(mainInstruction, Strings.ErrorGenericTitle, explanation) {
                CommonIcon = TaskDialogIcon.Stop,
                IsExpanded = false
            };

            if (!string.IsNullOrEmpty(errorMessage)) {
                dlg.ExpandedInformation = Strings.ErrorGenericInfoText + errorMessage;
                dlg.ExpandedControlText = Strings.ErrorGenericInfoButton;
            }

            dlg.Show(this);
        }

        /// <summary>
        /// Ensures that the main form is visible (either closing the fullscreen mode or reactivating from task icon).
        /// </summary>
        public void EnsureMainFormVisible() {
            //Reset special modes
            IsFullscreen = false;
            ClickThroughEnabled = false;
            Opacity = 1.0;

            //Ensure main form is shown
            WindowState = FormWindowState.Normal;
            Show();
            Activate();
            TopMost = true;
        }

        /// <summary>
        /// Opens a confirmation dialog to confirm whether to reset the main form or not.
        /// </summary>
        public void ResetMainFormWithConfirmation() {
            var dlg = new TaskDialog(Strings.AskReset, Strings.AskResetTitle, Strings.AskResetContent);
            dlg.UseCommandLinks = true;
            dlg.CustomButtons = new CustomButton[] {
				new CustomButton(Result.OK, Strings.AskResetButtonOk),
				new CustomButton(Result.Cancel, Strings.ButtonCancel)
			};
            dlg.CommonIcon = TaskDialogIcon.Information;

            if (dlg.Show(this).CommonButton == Result.OK) {
                ResetMainForm();
            }
        }

        /// <summary>
        /// Resets the main form to its initial state.
        /// </summary>
        public void ResetMainForm() {
            //Reset form settings
            UnsetThumbnail();
            CloseSidePanel();

            //Reset location and size (edge of the screen, min size)
            Point nuLoc = Screen.PrimaryScreen.WorkingArea.Location;
            nuLoc.Offset(40, 40);
            Location = nuLoc;
            Size = MinimumSize;

            this.Show();
            this.Activate();
        }

    }
}