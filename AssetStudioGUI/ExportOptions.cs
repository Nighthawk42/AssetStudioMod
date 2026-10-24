using AssetStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AssetStudioGUI
{
    public partial class ExportOptions : Form
    {
        public ExportOptions()
        {
            InitializeComponent();
            assetGroupOptions.SelectedIndex = Properties.Settings.Default.assetGroupOption;
            filenameFormatComboBox.SelectedIndex = Properties.Settings.Default.filenameFormat;
            overwriteExistingFiles.Checked = Properties.Settings.Default.overwriteExistingFiles;
            restoreExtensionName.Checked = Properties.Settings.Default.restoreExtensionName;
            converttexture.Checked = Properties.Settings.Default.convertTexture;
            exportSpriteWithAlphaMask.Checked = Properties.Settings.Default.exportSpriteWithMask;
            convertAudio.Checked = Properties.Settings.Default.convertAudio;
            var defaultImageType = Properties.Settings.Default.convertType.ToString();
            ((RadioButton)panel1.Controls.Cast<Control>().First(x => x.Text == defaultImageType)).Checked = true;
            openAfterExport.Checked = Properties.Settings.Default.openAfterExport;
            var maxParallelTasks = Environment.ProcessorCount;
            var taskCount = Properties.Settings.Default.parallelExportCount;
            parallelExportUpDown.Maximum = maxParallelTasks;
            parallelExportUpDown.Value = taskCount <= 0 ? maxParallelTasks : Math.Min(taskCount, maxParallelTasks);
            parallelExportMaxLabel.Text += maxParallelTasks;
            parallelExportCheckBox.Checked = Properties.Settings.Default.parallelExport;
            rawByteArrayFromMono.Checked = Properties.Settings.Default.rawByteArrayFromMono;

            l2dModelGroupComboBox.SelectedIndex = (int)Properties.Settings.Default.l2dModelGroupOption;
            l2dAssetSearchByFilenameCheckBox.Checked = Properties.Settings.Default.l2dAssetSearchByFilename;
            var defaultMotionMode = Properties.Settings.Default.l2dMotionMode.ToString();
            ((RadioButton)l2dMotionExportMethodPanel.Controls.Cast<Control>().First(x => x.AccessibleName == defaultMotionMode)).Checked = true;
            l2dForceBezierCheckBox.Checked = Properties.Settings.Default.l2dForceBezier;

            SetFromModelSettings(Studio.ModelSettings);
        }

        private void OKbutton_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.assetGroupOption = assetGroupOptions.SelectedIndex;
            Properties.Settings.Default.filenameFormat = filenameFormatComboBox.SelectedIndex;
            Properties.Settings.Default.overwriteExistingFiles = overwriteExistingFiles.Checked;
            Properties.Settings.Default.restoreExtensionName = restoreExtensionName.Checked;
            Properties.Settings.Default.convertTexture = converttexture.Checked;
            Properties.Settings.Default.exportSpriteWithMask = exportSpriteWithAlphaMask.Checked;
            Properties.Settings.Default.convertAudio = convertAudio.Checked;
            var checkedImageType = (RadioButton)panel1.Controls.Cast<Control>().First(x => ((RadioButton)x).Checked);
            Properties.Settings.Default.convertType = (ImageFormat)Enum.Parse(typeof(ImageFormat), checkedImageType.Text);
            Properties.Settings.Default.openAfterExport = openAfterExport.Checked;
            Properties.Settings.Default.parallelExport = parallelExportCheckBox.Checked;
            Properties.Settings.Default.parallelExportCount = (int)parallelExportUpDown.Value;
            Properties.Settings.Default.rawByteArrayFromMono = rawByteArrayFromMono.Checked;

            Properties.Settings.Default.l2dModelGroupOption = (CubismLive2DExtractor.Live2DModelGroupOption)l2dModelGroupComboBox.SelectedIndex;
            Properties.Settings.Default.l2dAssetSearchByFilename = l2dAssetSearchByFilenameCheckBox.Checked;
            var checkedMotionMode = (RadioButton)l2dMotionExportMethodPanel.Controls.Cast<Control>().First(x => ((RadioButton)x).Checked);
            Properties.Settings.Default.l2dMotionMode = (CubismLive2DExtractor.Live2DMotionMode)Enum.Parse(typeof(CubismLive2DExtractor.Live2DMotionMode), checkedMotionMode.AccessibleName);
            Properties.Settings.Default.l2dForceBezier = l2dForceBezierCheckBox.Checked;

            Studio.ModelSettings.ExportAllNodes = exportAllNodes.Checked;
            Studio.ModelSettings.ExportSkins = exportSkins.Checked;
            Studio.ModelSettings.ExportAnimations = exportAnimations.Checked;
            Studio.ModelSettings.ExportBlendShape = exportBlendShape.Checked;
            Studio.ModelSettings.ScaleFactor = (float)scaleFactor.Value;
            Properties.Settings.Default.modelExportSettings = Studio.ModelSettings.ToBase64();

            Properties.Settings.Default.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void parallelExportCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            parallelExportUpDown.Enabled = parallelExportCheckBox.Checked;
        }

        private void SetFromModelSettings(ModelExportSettings modelSettings)
        {
            exportAllNodes.Checked = modelSettings.ExportAllNodes;
            exportSkins.Checked = modelSettings.ExportSkins;
            exportAnimations.Checked = modelSettings.ExportAnimations;
            exportBlendShape.Checked = modelSettings.ExportBlendShape;
            scaleFactor.Value = Math.Clamp((decimal)modelSettings.ScaleFactor, scaleFactor.Minimum, scaleFactor.Maximum);
        }

        private void resetButton_Click(object sender, EventArgs e)
        {
            SetFromModelSettings(new ModelExportSettings());
        }
    }
}
