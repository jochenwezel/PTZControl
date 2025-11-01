using System;
using System.Linq;
using System.Windows.Forms;
using PTZControlBridge;

namespace PTZControlGUI
{
    public class MainForm : Form
    {
        private ComboBox cmbCams = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private TrackBar trkPan = new TrackBar { Dock = DockStyle.Top, TickFrequency = 10 };
        private TrackBar trkTilt = new TrackBar { Dock = DockStyle.Top, TickFrequency = 10 };
        private TrackBar trkZoom = new TrackBar { Dock = DockStyle.Top, TickFrequency = 1, Maximum = 100 };
        private Button btnApply = new Button { Dock = DockStyle.Top, Text = "Anwenden (Pan/Tilt/Zoom)" };
        private CheckBox chkMotion = new CheckBox { Dock = DockStyle.Top, Text = "Logitech Motion Control (XU) verwenden" };

        public MainForm()
        {
            Text = "PTZControl GUI (Bridge)";
            Controls.Add(btnApply);
            Controls.Add(chkMotion);
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
            chkMotion.CheckedChanged += ChkMotion_CheckedChanged;
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            try
            {
                var names = LogitechPtz.EnumerateCameras();
                foreach (var n in names) cmbCams.Items.Add(n);
                if (cmbCams.Items.Count > 0) cmbCams.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CmbCams_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string name = cmbCams.SelectedItem?.ToString() ?? "";
            TrySetRange(trkPan, name, CameraProperty.Pan);
            TrySetRange(trkTilt, name, CameraProperty.Tilt);
            TrySetRange(trkZoom, name, CameraProperty.Zoom);
        }

        private void TrySetRange(TrackBar trk, string cam, CameraProperty prop)
        {
            try
            {
                var r = LogitechPtz.GetRange(cam, prop);
                trk.Enabled = true;
                trk.Minimum = r.Min;
                trk.Maximum = r.Max;
                trk.SmallChange = Math.Max(1, r.Step);
                trk.LargeChange = Math.Max(5, r.Step * 5);
                trk.Value = Math.Clamp(r.Default, trk.Minimum, trk.Maximum);
            }
            catch
            {
                trk.Enabled = false;
            }
        }

        private void ChkMotion_CheckedChanged(object? sender, EventArgs e)
        {
            string name = cmbCams.SelectedItem?.ToString() ?? "";
            try
            {
                LogitechPtz.UseLogitechMotionControl(name, chkMotion.Checked);
            }
            catch (NotSupportedException nse)
            {
                MessageBox.Show(this, nse.Message, "Nicht verfügbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                chkMotion.Checked = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                chkMotion.Checked = false;
            }
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            string name = cmbCams.SelectedItem?.ToString() ?? "";
            try
            {
                LogitechPtz.SetPanTiltZoom(name,
                    trkPan.Enabled ? trkPan.Value : (int?)null,
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