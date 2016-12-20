using KevinHelper;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        string record_pattern;
        string file_checksum;
        public frmMain()
        {
            InitializeComponent();

            colorStatus = new Dictionary<FileCheck.FileStatus, Color>();
            colorStatus[FileCheck.FileStatus.NONE] = Color.Black;
            colorStatus[FileCheck.FileStatus.SUCCESS] = Color.RoyalBlue;
            colorStatus[FileCheck.FileStatus.FAILED] = Color.Red;
            colorStatus[FileCheck.FileStatus.NOTFOUND] = Color.Gray;
        }

        private void frmMain_DragDrop(object sender, DragEventArgs e)
        {
            file_checksum = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];

            bool check = CheckInput();
            if (check) PopularInput();
        }
        private void frmMain_DragEnter(object sender, DragEventArgs e)
        {
            string file = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
            bool is_file = File.Exists(file);
            if (is_file && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private async void btnCheck_Click(object sender, EventArgs e)
        {
            HashCalculator hcal = new HashCalculator();
            hcal.algorithm = algorithmType;
            hcal.files = listInput.Select(f => f.full_path).ToList();
            hcal.progressCallback += Hcal_progressCallback;
            hcal.resultCallback += Hcal_resultCallback;

            await hcal.ComputeHashAsync();

        }

        private void Hcal_resultCallback(string checksum, int index, Exception ex)
        {
            var file = listInput[index];
            if (ex != null)
            {
                Console.WriteLine(ex.Message);
                file.Status = FileCheck.FileStatus.NOTFOUND;
            }
            else
            {
                file.Checksum_new = checksum;
                file.Status = file.Checksum_new == file.Checksum ? FileCheck.FileStatus.SUCCESS : FileCheck.FileStatus.FAILED;
            }

            olvFiles.RefreshObject(file);
            olvFiles.EnsureModelVisible(file);
        }

        private void Hcal_progressCallback(string file_name, int percent)
        {
            progressBar1.Value = percent;
            this.Text = string.Format("{0}% - {1}", percent, Path.GetFileName(file_name));
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

            if (algorithmType != AlgorithmType.NONE)
                lbDir.Text = algorithmType + " - " + file_checksum;
            else
                lbDir.Text = "Error!";

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

        private void olvFiles_FormatRow(object sender, BrightIdeasSoftware.FormatRowEventArgs e)
        {
            FileCheck file = (FileCheck)e.Model;
            e.Item.ForeColor = colorStatus[file.Status];
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
