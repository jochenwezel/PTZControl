using System;
using System.Linq;
using System.Windows.Forms;
using PTZControl.Uvc;
using DirectShowLib;

namespace PTZControlGUI
{
    public class MainForm : Form
    {
        private ComboBox cmbCams = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private TrackBar trkPan = new TrackBar { Dock = DockStyle.Top, TickFrequency = 10 };
        private TrackBar trkTilt = new TrackBar { Dock = DockStyle.Top, TickFrequency = 10 };
        private TrackBar trkZoom = new TrackBar { Dock = DockStyle.Top, TickFrequency = 1, Maximum = 100 };
        private Button btnApply = new Button { Dock = DockStyle.Top, Text = "Anwenden (Pan/Tilt/Zoom)" };

        public MainForm()
        {
            Text = "PTZControl GUI (UVC)";
            Controls.Add(btnApply);
            Controls.Add(new Label { Text = "Zoom", Dock = DockStyle.Top });
            Controls.Add(trkZoom);
            Controls.Add(new Label { Text = "Tilt", Dock = DockStyle.Top });
            Controls.Add(trkTilt);
            Controls.Add(new Label { Text = "Pan", Dock = DockStyle.Top });
            Controls.Add(trkPan);
            Controls.Add(cmbCams);

            Load += MainForm_Load;
            btnApply.Click += BtnApply_Click;
            cmbCams.SelectedIndexChanged += CmbCams_SelectedIndexChanged;
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            var cams = UvcCamera.Enumerate();
            foreach (var c in cams) cmbCams.Items.Add(c.Name);
            if (cmbCams.Items.Count > 0) cmbCams.SelectedIndex = 0;
        }

        private void CmbCams_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string name = cmbCams.SelectedItem?.ToString() ?? "";
            try
            {
                var (min, max, step, def) = UvcCamera.GetRange(name, CameraControlProperty.Pan);
                trkPan.Minimum = min; trkPan.Maximum = max; trkPan.SmallChange = step; trkPan.LargeChange = step * 5; trkPan.Value = Math.Clamp(def, min, max);
            }
            catch { trkPan.Enabled = false; }

            try
            {
                var (min, max, step, def) = UvcCamera.GetRange(name, CameraControlProperty.Tilt);
                trkTilt.Minimum = min; trkTilt.Maximum = max; trkTilt.SmallChange = step; trkTilt.LargeChange = step * 5; trkTilt.Value = Math.Clamp(def, min, max);
            }
            catch { trkTilt.Enabled = false; }

            try
            {
                var (min, max, step, def) = UvcCamera.GetRange(name, CameraControlProperty.Zoom);
                trkZoom.Minimum = min; trkZoom.Maximum = max; trkZoom.SmallChange = step; trkZoom.LargeChange = step * 5; trkZoom.Value = Math.Clamp(def, min, max);
            }
            catch { trkZoom.Enabled = false; }
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            string name = cmbCams.SelectedItem?.ToString() ?? "";
            try
            {
                UvcCamera.SetPanTiltZoom(name, trkPan.Enabled ? trkPan.Value : (int?)null,
                                              trkTilt.Enabled ? trkTilt.Value : (int?)null,
                                              trkZoom.Enabled ? trkZoom.Value : (int?)null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}