using KevinHelper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static KevinHelper.HashCalculator;

namespace HashChecker
{
    public partial class frmMain : Form
    {
        const string record_pattern_crc32 = @"^(\w{8}) \*(.+)$";
        const string record_pattern_md5 = @"^(\w{32}) \*(.+)$";
        const string record_pattern_sha1 = @"^(\w{40}) \*(.+)$";

        AlgorithmType algorithmType;
        List<FileCheck> listInput = new List<FileCheck>();
        Dictionary<FileCheck.FileStatus, Color> colorStatus;
        HashCalculator hcal = new HashCalculator();

        string record_pattern, file_checksum, folderCheck;

        public frmMain()
        {
            InitializeComponent();

            colorStatus = new Dictionary<FileCheck.FileStatus, Color>();
            colorStatus[FileCheck.FileStatus.NONE] = Color.Black;
            colorStatus[FileCheck.FileStatus.SUCCESS] = Color.RoyalBlue;
            colorStatus[FileCheck.FileStatus.FAILED] = Color.Red;
            colorStatus[FileCheck.FileStatus.NOTFOUND] = Color.Gray;
            cbHashType.SelectedIndex = 0;
        }
        private void frmMain_DragDrop(object sender, DragEventArgs e)
        {
            file_checksum = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];

            btnCheck.Enabled = btnSave.Enabled = false;
            if (File.Exists(file_checksum))
            {
                bool check = CheckInput();
                if (check) PopularInput();

                btnCheck.Enabled = true;
            }
            else// if (Directory.Exists(file_checksum))
            {
                lbDir.Text = folderCheck = file_checksum;
                olvFiles.ClearObjects();

                btnSave.Enabled = true;
            }
        }
        private void frmMain_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private async void btnCheck_Click(object sender, EventArgs e)
        {
            if (!hcal.IsRunning)
            {
                foreach (var f in listInput)
                    f.Status = FileCheck.FileStatus.NONE;
                olvFiles.SetObjects(listInput);

                hcal.algorithm = algorithmType;
                hcal.InputFiles = listInput.Select(f => f.full_path).ToList();
                hcal.progressIndicator = new Progress<object[]>(Hcal_progressCallback);
                hcal.resultCallback = new Progress<object[]>(Hcal_resultCallback);

                btnCheck.Text = "Stop";
                await hcal.ComputeHashAsync();
                olvFiles.BuildList();
                btnCheck.Text = "Check";

                int m = listInput.Count(fi => fi.Status == FileCheck.FileStatus.SUCCESS);
                int ff = listInput.Count(fi => fi.Status == FileCheck.FileStatus.FAILED);
                int n = listInput.Count(fi => fi.Status == FileCheck.FileStatus.NOTFOUND);
                lbResult.Text = $"Match: {m}, fail: {ff}, not found: {n}";
            }
            else
                hcal.Stop();
        }
        private async void btnSave_Click(object sender, EventArgs e)
        {
            if (hcal.IsRunning)
            {
                hcal.Stop();
                return;
            }

            listInput.Clear();
            JobWalkDirectories walk = new JobWalkDirectories
            {
                searchFols = new List<JobWalkDirectories.SearchPath>
                {
                    new JobWalkDirectories.SearchPath { folder = folderCheck, m_subFolder = true }
                }
            };
            List<FileInfo> result = await walk.WalkThroughAsync();
            result.ForEach(f => listInput.Add(new FileCheck { full_path = f.FullName }));

            olvFiles.SetObjects(listInput);

            hcal.algorithm = algorithmType = cbHashType.SelectedIndex == 0 ? AlgorithmType.CRC32 :
                cbHashType.SelectedIndex == 1 ? AlgorithmType.MD5 : AlgorithmType.SHA1;

            hcal.progressIndicator = new Progress<object[]>(Hcal_progressCallback);
            hcal.resultCallback = new Progress<object[]>(Hash_resultCallback);
            hcal.InputFiles = result.Select(f => f.FullName).ToList();

            btnSave.Text = "Stop";
            await hcal.ComputeHashAsync();
            btnSave.Text = "Save";
            olvFiles.BuildList();

            string root = Path.GetDirectoryName(folderCheck);
            using (StreamWriter sw = new StreamWriter(folderCheck + "." + algorithmType))
            {
                foreach (var child in listInput)
                {
                    sw.WriteLine($"{child.Checksum} *{FileUtils.GetRelativePath(child.full_path, root)}");
                }
            }

            MessageBox.Show("Save hash files successfully.");
        }
        private void olvFiles_FormatRow(object sender, BrightIdeasSoftware.FormatRowEventArgs e)
        {
            FileCheck file = (FileCheck)e.Model;
            e.Item.ForeColor = colorStatus[file.Status];
        }

        private void Hcal_resultCallback(object[] o)
        {
            string checksum = (string)o[0];
            int index = (int)o[1];
            Exception ex = (Exception)o[2];

            var file = listInput[index];
            if (ex != null)
            {
                //Console.WriteLine(ex.Message);
                file.Status = FileCheck.FileStatus.NOTFOUND;
            }
            else
            {
                file.Checksum_new = checksum;
                file.Status = string.Compare(file.Checksum_new, file.Checksum, true) == 0 ? FileCheck.FileStatus.SUCCESS : FileCheck.FileStatus.FAILED;
            }
        }
        private void Hcal_progressCallback(object[] o)
        {
            string file_name = (string)o[0];
            int percent = (int)o[1];

            progressBar1.Value = percent;
            this.Text = $"{percent}% - {Path.GetFileName(file_name)}";
        }
        private void Hash_resultCallback(object[] o)
        {
            string checksum = (string)o[0];
            int index = (int)o[1];
            Exception ex = (Exception)o[2];

            var file = listInput[index];
            if (ex != null)
            {
                Console.WriteLine(ex.Message);
                file.Status = FileCheck.FileStatus.FAILED;
            }
            else
            {
                file.Checksum = checksum;
                file.Status = FileCheck.FileStatus.SUCCESS;
            }
        }

        bool CheckInput()
        {
            string line;
            using (var stream = File.OpenText(file_checksum)) { line = stream.ReadLine(); }

            algorithmType = AlgorithmType.NONE;

            if (Regex.IsMatch(line, record_pattern_crc32))
            {
                record_pattern = record_pattern_crc32;
                algorithmType = AlgorithmType.CRC32;
            }
            else if (Regex.IsMatch(line, record_pattern_md5))
            {
                record_pattern = record_pattern_md5;
                algorithmType = AlgorithmType.MD5;
            }
            else if (Regex.IsMatch(line, record_pattern_sha1))
            {
                record_pattern = record_pattern_sha1;
                algorithmType = AlgorithmType.SHA1;
            }

            cbHashType.SelectedIndex = (int)algorithmType - 1;
            lbDir.Text = algorithmType != AlgorithmType.NONE ? lbDir.Text = file_checksum: "Error!";

            return algorithmType != AlgorithmType.NONE;
        }
        void PopularInput()
        {
            string folder = Path.GetDirectoryName(file_checksum);
            listInput.Clear();
            using (var stream = File.OpenText(file_checksum))
            {
                while (!stream.EndOfStream)
                {
                    string line = stream.ReadLine();
                    Match m = Regex.Match(line, record_pattern);
                    if (m.Success)
                    {
                        listInput.Add(new FileCheck
                        {
                            full_path =  Path.Combine(folder, m.Groups[2].Value),
                            Checksum = m.Groups[1].Value
                        });
                    }
                }
            }

            olvFiles.SetObjects(listInput);
        }
    }

    class FileCheck
    {
        public string full_path { get; set; }
        public string Name { get { return Path.GetFileName(full_path); } }
        public string Dir { get { return Path.GetDirectoryName(full_path); } }

        public string Checksum { get; set; }
        public string Checksum_new { get; set; }
        public FileStatus Status { get; set; }

        public enum FileStatus
        {
            NONE,
            SUCCESS,
            FAILED,
            NOTFOUND
        }
    }
}
