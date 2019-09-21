using KevinHelper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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
        const string record_pattern_crc32 = @"^(\w{8}) \*?(.+)$";
        const string record_pattern_md5 = @"^(\w{32}) \*?(.+)$";
        const string record_pattern_sha1 = @"^(\w{40}) \*?(.+)$";

        AlgorithmType algorithmType;
        List<FileCheck> listInput = new List<FileCheck>();
        List<FileCheck> listGroup = new List<FileCheck>();
        Dictionary<FileCheck.FileStatus, Color> colorStatus;
        HashCalculator hcal = new HashCalculator();

        string record_pattern, file_checksum;

        public frmMain()
        {
            InitializeComponent();

            this.Text = "Hash Checker v2.1 - 2018/10/28";
            colorStatus = new Dictionary<FileCheck.FileStatus, Color>();
            colorStatus[FileCheck.FileStatus.NONE] = Color.Black;
            colorStatus[FileCheck.FileStatus.SUCCESS] = Color.RoyalBlue;
            colorStatus[FileCheck.FileStatus.FAILED] = Color.Red;
            colorStatus[FileCheck.FileStatus.NOTFOUND] = Color.Gray;
            cbHashType.SelectedIndex = 0;
        }
        private void frmMain_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            
            btnCheck.Enabled = btnSave.Enabled = false;
            if (files.Length == 1 && File.Exists(files[0]) && !string.IsNullOrEmpty(Path.GetExtension(files[0])) &&
                Enum.GetNames(typeof(AlgorithmType)).Contains(Path.GetExtension(files[0]).Substring(1).ToUpper()))
            {
                file_checksum = files[0];
                bool check = CheckInput();
                if (check) PopularInput();

                btnCheck.Enabled = check;
            }
            else
            {
                foreach (string file in files)
                {
                    listGroup.Add(new FileCheck
                    {
                        full_path = file,
                        IsFolder = Directory.Exists(file)
                    });
                    //lbDir.Text = folderCheck = file_checksum;
                }
                olvFiles.SetObjects(listGroup);

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
            olvFiles.ClearObjects();

            foreach (FileCheck folder in listGroup)
            {
                if (folder.IsFolder)
                {
                    JobWalkDirectories walk = new JobWalkDirectories
                    {
                        searchFols = new List<JobWalkDirectories.SearchPath>
                        {
                            new JobWalkDirectories.SearchPath { folder = folder.full_path, m_subFolder = true }
                        }
                    };
                    List<FileInfo> result = await walk.WalkThroughAsync();
                    result.ForEach(f => listInput.Add(new FileCheck { full_path = f.FullName, root = folder.Dir }));
                }
                else
                    listInput.Add(new FileCheck { full_path = folder.full_path, root = null });

            }

            olvFiles.AddObjects(listInput);

            hcal.algorithm = algorithmType = cbHashType.SelectedIndex == 0 ? AlgorithmType.CRC32 :
                cbHashType.SelectedIndex == 1 ? AlgorithmType.MD5 : AlgorithmType.SHA1;

            hcal.progressIndicator = new Progress<object[]>(Hcal_progressCallback);
            hcal.resultCallback = new Progress<object[]>(Hash_resultCallback);
            hcal.InputFiles = listInput.Select(f => f.full_path).ToList();

            btnSave.Text = "Stop";
            await hcal.ComputeHashAsync();
            btnSave.Text = "Save";
            olvFiles.BuildList();

            using (StreamWriter sw = new StreamWriter(listGroup[0].full_path + "." + algorithmType))
                foreach (var child in listInput)
                    sw.WriteLine($"{child.Checksum} {FileUtils.GetRelativePath(child.full_path, child.root)}");

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
            int index = (int)o[2];

            progressBar1.Value = percent;
            this.Text = $"{percent}% - {Path.GetFileName(file_name)}";

            //refresh grid
            for (int i = 0; i < olvFiles.RowsPerPage; ++i)
                olvFiles.RefreshItem(olvFiles.GetItem(olvFiles.TopItemIndex + i));
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

        private void btnClear_Click(object sender, EventArgs e)
        {
            olvFiles.ClearObjects();
            listInput.Clear();
            listGroup.Clear();
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

        private void olvFiles_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && olvFiles.SelectedObjects.Count > 0)
            {
                foreach (FileCheck o in olvFiles.SelectedObjects)
                {
                    listGroup.Remove(o);
                }
                olvFiles.SetObjects(listGroup);
            }
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
        public string root { get; set; }
        public string Name { get { return Path.GetFileName(full_path); } }
        public string Dir { get { return Path.GetDirectoryName(full_path); } }
        public bool IsFolder { get; set; }

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
