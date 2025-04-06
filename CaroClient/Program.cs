using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CaroClient
{
    public class Common
    {
        public const int BOARD_SIZE = 15;
        public const int CELL_SIZE = 40;
        public const int WINNING_COUNT = 5;

        public enum CellState { Empty, X, O }
        public enum GameStatus { Waiting, Playing, GameOver }

        public static string FormatMessage(string command, string data)
        {
            return $"{command}|{data}";
        }

        public static void ParseMessage(string message, out string command, out string data)
        {
            string[] parts = message.Split('|');
            command = parts[0];
            data = parts.Length > 1 ? parts[1] : "";
        }
    }

    public class CaroClient : Form
    {
        private TcpClient client;
        private SslStream sslStream;
        private Thread listenThread;
        private Panel boardPanel;
        private Label statusLabel;
        private Label roomLabel;
        private TextBox chatInput;
        private Button sendButton;
        private Button surrenderButton;
        private ListBox chatBox;
        private Button connectButton;
        private TextBox serverInput;
        private TextBox passwordInput;
        private Common.CellState[,] board = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        private Common.GameStatus gameStatus = Common.GameStatus.Waiting;
        private Common.CellState playerRole = Common.CellState.Empty;
        private bool isMyTurn = false;
        private int roomId = -1;
        private int lastMoveRow = -1;
        private int lastMoveCol = -1;
        private NumericUpDown receiveTimeoutInput;
        private NumericUpDown sendTimeoutInput;
        private Label receiveTimeoutLabel;
        private Label sendTimeoutLabel;
        private Label receiveCountdownLabel;
        private Label sendCountdownLabel;
        private System.Windows.Forms.Timer timeoutTimer;
        private DateTime lastReceiveTime;
        private DateTime lastSendTime;


        public CaroClient()
        {
            InitializeComponents();
            InitializeBoard();
            passwordInput.Focus();
        }

        private void InitializeComponents()
        {
            this.Text = "Caro Game Client";
            this.Size = new Size(800, 700);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // Server connection controls
            Label serverLabel = new Label
            {
                Text = "Server Address:",
                Location = new Point(20, 2),
                AutoSize = true
            };

            serverInput = new TextBox
            {
                Text = "127.0.0.1",
                Location = new Point(120, 2),
                Size = new Size(120, 20)
            };

            Label passwordLabel = new Label
            {
                Text = "Password:",
                Location = new Point(20, 30),
                AutoSize = true
            };

            passwordInput = new TextBox
            {
                Location = new Point(120, 30),
                Size = new Size(120, 20),
                PasswordChar = '*'
            };

            connectButton = new Button
            {
                Text = "Connect",
                Location = new Point(250, 20),
                Size = new Size(80, 23)
            };
            connectButton.Click += Connect_Click;

            statusLabel = new Label
            {
                Text = "Not connected",
                Location = new Point(20, 50),
                Size = new Size(300, 20)
            };

            roomLabel = new Label
            {
                Text = "Room: None",
                Location = new Point(20, 70),
                Size = new Size(200, 20)
            };
            surrenderButton = new Button
            {
                Text = "Surrender",
                Location = new Point(350, 20),
                Size = new Size(80, 23)
            };
            surrenderButton.Click += Surrender_Click;

            // Game board
            boardPanel = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(Common.BOARD_SIZE * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE),
                BorderStyle = BorderStyle.FixedSingle
            };
            boardPanel.Paint += BoardPanel_Paint;
            boardPanel.MouseClick += BoardPanel_MouseClick;

            // Chat controls
            chatBox = new ListBox
            {
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 50, 100),
                Size = new Size(250, 400),
                IntegralHeight = false
            };

            chatInput = new TextBox
            {
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 50, 510),
                Size = new Size(170, 20)
            };
            chatInput.KeyPress += (s, e) => { if (e.KeyChar == (char)13) SendChatMessage(); };

            sendButton = new Button
            {
                Text = "Send",
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 230, 509),
                Size = new Size(70, 23)
            };
            sendButton.Click += (s, e) => SendChatMessage();

            Label receiveTimeoutTextLabel = new Label
            {
                Text = "Receive Timeout:",
                Location = new Point(450, 10),
                AutoSize = true
            };

            receiveTimeoutInput = new NumericUpDown
            {
                Minimum = 1000,
                Maximum = 60000,
                Increment = 1000,
                Value = 15000,
                Location = new Point(receiveTimeoutTextLabel.Right + 20, 10),
                Size = new Size(80, 20)
            };

            receiveCountdownLabel = new Label
            {
                Text = "15.0s",
                Location = new Point(receiveTimeoutInput.Right + 20, 10),
                AutoSize = true,
                ForeColor = Color.Green
            };

            Label sendTimeoutTextLabel = new Label
            {
                Text = "Send Timeout:",
                Location = new Point(receiveTimeoutTextLabel.Left, 35),
                AutoSize = true
            };

            sendTimeoutInput = new NumericUpDown
            {
                Minimum = 1000,
                Maximum = 60000,
                Increment = 1000,
                Value = 15000,
                Location = new Point(sendTimeoutTextLabel.Right + 20, 35),
                Size = new Size(80, 20)
            };

            sendCountdownLabel = new Label
            {
                Text = "15.0s",
                Location = new Point(sendTimeoutInput.Right + 20, 35),
                AutoSize = true,
                ForeColor = Color.Green
            };

            this.Controls.Add(serverLabel);
            this.Controls.Add(serverInput);
            this.Controls.Add(passwordLabel);
            this.Controls.Add(passwordInput);
            this.Controls.Add(connectButton);
            this.Controls.Add(statusLabel);
            this.Controls.Add(roomLabel);
            this.Controls.Add(boardPanel);
            this.Controls.Add(chatBox);
            this.Controls.Add(chatInput);
            this.Controls.Add(sendButton);
            this.Controls.Add(surrenderButton);

            this.FormClosing += (s, e) => DisconnectFromServer();

            this.Controls.Add(receiveTimeoutTextLabel);
            this.Controls.Add(receiveTimeoutInput);
            this.Controls.Add(receiveCountdownLabel);
            this.Controls.Add(sendTimeoutTextLabel);
            this.Controls.Add(sendTimeoutInput);
            this.Controls.Add(sendCountdownLabel);
            timeoutTimer = new System.Windows.Forms.Timer
            {
                Interval = 100
            };
            timeoutTimer.Tick += TimeoutTimer_Tick;
            timeoutTimer.Start();

        }

        private void Connect_Click(object sender, EventArgs e)
        {
            if (client == null)
            {
                ConnectToServer();
            }
            else
            {
                DisconnectFromServer();
            }
        }
        private void TimeoutTimer_Tick(object sender, EventArgs e)
        {
            if (client != null && client.Connected)
            {
                // Cập nhật bộ đếm ngược receive
                if (lastReceiveTime != DateTime.MinValue)
                {
                    double receiveRemaining = ((double)receiveTimeoutInput.Value - (DateTime.Now - lastReceiveTime).TotalMilliseconds) / 1000;
                    receiveCountdownLabel.Text = $"{Math.Max(0, receiveRemaining):0.0}s";
                    receiveCountdownLabel.ForeColor = receiveRemaining < 5 ? Color.Red : Color.Green;
                }

                // Cập nhật bộ đếm ngược send
                if (lastSendTime != DateTime.MinValue)
                {
                    double sendRemaining = ((double)sendTimeoutInput.Value - (DateTime.Now - lastSendTime).TotalMilliseconds) / 1000;
                    sendCountdownLabel.Text = $"{Math.Max(0, sendRemaining):0.0}s";
                    sendCountdownLabel.ForeColor = sendRemaining < 5 ? Color.Red : Color.Green;
                }
            }
            else
            {
                receiveCountdownLabel.Text = "0.0s";
                sendCountdownLabel.Text = "0.0s";
            }
        }

        // Kết nối đến server

        private void ConnectToServer()
        {
            try
            {
                string serverAddress = serverInput.Text.Trim();
                string password = passwordInput.Text.Trim();

                if (string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Please enter a password.", "Password Required",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Create new connection
                TcpClient newClient = new TcpClient();
                newClient.Connect(serverAddress, 8888);

                // Set timeouts to prevent hanging
                newClient.ReceiveTimeout = (int)(double)receiveTimeoutInput.Value;
                newClient.SendTimeout = (int)(double)sendTimeoutInput.Value;

                SslStream newSslStream = new SslStream(newClient.GetStream(), false,
                                                     ValidateServerCertificate, null);

                // Authenticate with proper protocols
                newSslStream.AuthenticateAsClient(serverAddress, null,
                                                SslProtocols.Tls12, false);

                this.client = newClient;
                this.sslStream = newSslStream;

                // Send password immediately
                SendMessage(Common.FormatMessage("PASSWORD", password));

                // Start listener thread
                this.listenThread = new Thread(ListenForServerMessages);
                listenThread.IsBackground = true;
                listenThread.Start();

                UpdateUIOnConnected();
                Console.WriteLine($"[Client] Connected to server at {serverAddress}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}",
                                "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                DisconnectFromServer();
            }
        }
        private void UpdateUIOnConnected()
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() =>
                {
                    statusLabel.Text = "Connected. Waiting for room...";
                    connectButton.Text = "Disconnect";
                    chatInput.Enabled = true;
                    sendButton.Enabled = true;
                }));
            }
            else
            {
                statusLabel.Text = "Connected. Waiting for room...";
                connectButton.Text = "Disconnect";
                chatInput.Enabled = true;
                sendButton.Enabled = true;
            }
        }
        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // Bỏ qua lỗi chứng chỉ trong môi trường phát triển (không khuyến khích trong production)
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                // Lỗi mismatch tên chứng chỉ, bỏ qua khi phát triển
                return true;
            }
            else
            {
                MessageBox.Show($"SSL Certificate error: {sslPolicyErrors}", "SSL Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }


        // Ngắt kết nối từ server

        private void DisconnectFromServer()
        {
            try
            {
                // Check if form is being disposed
                if (this.IsDisposed) return;

                // Send disconnect message if connected
                if (sslStream != null && client != null && client.Connected)
                {
                    try
                    {
                        SendMessage(Common.FormatMessage("DISCONNECT", "Client disconnecting"));
                    }
                    catch { }
                }
            }
            finally
            {
                // Close connections safely
                try { sslStream?.Close(); } catch { }
                try { client?.Close(); } catch { }

                // Reset state only if form isn't disposed
                if (!this.IsDisposed)
                {
                    ResetClientState();
                    UpdateUIOnDisconnected();
                }
            }
        }

        private void ResetClientState()
        {
            client = null;
            sslStream = null;
            listenThread = null; // Thread sẽ tự kết thúc khi stream đóng
            gameStatus = Common.GameStatus.Waiting;
            playerRole = Common.CellState.Empty;
            isMyTurn = false;
            roomId = -1;
        }

        private void UpdateUIOnDisconnected()
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() =>
                {
                    statusLabel.Text = "Disconnected";
                    connectButton.Text = "Connect";
                    roomLabel.Text = "Room: None";
                    chatInput.Enabled = false;
                    sendButton.Enabled = false;
                    InitializeBoard();
                    boardPanel.Invalidate();
                }));
            }
            else
            {
                statusLabel.Text = "Disconnected";
                connectButton.Text = "Connect";
                roomLabel.Text = "Room: None";
                chatInput.Enabled = false;
                sendButton.Enabled = false;
                InitializeBoard();
                boardPanel.Invalidate();
            }
        }
        private void ListenForServerMessages()
        {
            byte[] buffer = new byte[4096]; // Larger buffer

            try
            {
                while (client != null && client.Connected && !this.IsDisposed)
                {
                    try
                    {
                        int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;
                        lastReceiveTime = DateTime.Now;
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        string[] messages = receivedData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string message in messages)
                        {
                            if (this.IsDisposed) return;

                            string command, data;
                            Common.ParseMessage(message, out command, out data);

                            Console.WriteLine($"[Client {roomId}] Received: {command} | {data}");

                            this.Invoke((MethodInvoker)delegate
                            {
                                if (!this.IsDisposed)
                                {
                                    ProcessServerMessage(command, data);
                                }
                            });
                        }
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException)
                    {
                        HandleDisconnection("Connection lost: " + ex.Message);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                {
                    HandleDisconnection(ex.Message);
                }
            }
        }
        private void ProcessServerMessage(string command, string data)
        {
            try
            {
                if (data.Contains("|"))
                {
                    var subParts = data.Split('|');
                    ProcessServerMessage(command, subParts[0]); // Process first part
                    ProcessServerMessage(subParts[1], subParts.Length > 2 ? subParts[2] : ""); // Process second part
                    return;
                }
                switch (command)
                {
                    case "PASSWORD_REQUIRED":
                        // Resend the password if requested
                        SendMessage(Common.FormatMessage("PASSWORD", passwordInput.Text));
                        break;

                    case "PASSWORD_ACCEPTED":
                        // Password accepted, proceed normally
                        break;

                    case "PASSWORD_REJECTED":
                        MessageBox.Show("Incorrect password", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        DisconnectFromServer();
                        break;

                    case "DISCONNECT":
                        HandleOpponentDisconnection(data);
                        break;

                    case "ROLE":
                        var roleParts = data.Split(',');
                        if (roleParts.Length >= 2)
                        {
                            playerRole = roleParts[0] == "X" ? Common.CellState.X : Common.CellState.O;
                            roomId = int.Parse(roleParts[1]);
                            UpdateRoomLabel($"Room: {roomId}");
                            UpdateStatus($"You are {playerRole} in Room {roomId}");
                        }
                        break;

                    case "START":
                        gameStatus = Common.GameStatus.Playing;
                        isMyTurn = (playerRole == Common.CellState.X);
                        UpdateStatus(isMyTurn ? "Your turn!" : "Opponent's turn");
                        break;

                    case "MOVE":
                        var moveParts = data.Split(',');
                        int row = int.Parse(moveParts[0]);
                        int col = int.Parse(moveParts[1]);
                        int playerIndex = int.Parse(moveParts[2]);

                        board[row, col] = playerIndex == 0 ? Common.CellState.X : Common.CellState.O;
                        lastMoveRow = row;
                        lastMoveCol = col;
                        isMyTurn = (playerIndex == 0 && playerRole == Common.CellState.O) ||
                                  (playerIndex == 1 && playerRole == Common.CellState.X);
                        UpdateBoard();
                        UpdateStatus(isMyTurn ? "Your turn!" : "Opponent's turn");
                        break;

                    case "GAMEOVER":
                        gameStatus = Common.GameStatus.GameOver;
                        var result = MessageBox.Show($"Game over: {data} Restart?", "Game Over",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            SendMessage(Common.FormatMessage("RESTART", ""));
                        }
                        else
                        {
                            DisconnectFromServer();
                        }
                        break;

                    case "RESTART":

                        InitializeBoard();
                        lastMoveRow = -1;
                        lastMoveCol = -1;
                        gameStatus = Common.GameStatus.Playing;
                        isMyTurn = (playerRole == Common.CellState.X);
                        UpdateStatus(isMyTurn ? "Game restarted. Your turn!" : "Game restarted. Opponent's turn...");
                        UpdateBoard();
                        break;

                    case "CHAT":
                        AddChatMessage(data);
                        break;

                    default:
                        Console.WriteLine($"[Client {roomId}] Unknown command: {command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client {roomId}] Message processing error: {ex.Message}");
            }
        }
        private void HandleOpponentDisconnection(string reason)
        {
            Console.WriteLine($"[Client {roomId}] Opponent disconnected: {reason}");

            this.Invoke((MethodInvoker)delegate
            {
                // Reset giao diện bàn cờ
                ResetBoard();

                // Cập nhật trạng thái
                UpdateStatus($"Đối thủ đã thoát: {reason}. Đang chờ người chơi mới...");

                // Hiển thị thông báo cho người chơi
                MessageBox.Show($"Đối thủ đã thoát: {reason}\nBạn sẽ được ghép với người chơi mới khi có kết nối",
                              "Đối thủ thoát",
                              MessageBoxButtons.OK,
                              MessageBoxIcon.Information);

                // Chuyển về trạng thái chờ
                gameStatus = Common.GameStatus.Waiting;
                playerRole = Common.CellState.Empty;
            });
        }

        private void ResetBoard()
        {
            InitializeBoard(); // Reset mảng board
            UpdateBoard(); // Vẽ lại bàn cờ

            // Cập nhật trạng thái
            UpdateStatus("Đối thủ đã thoát. Đang chờ người chơi mới...");
        }

        private void HandleDisconnection(string reason)
        {
            Console.WriteLine($"[Client {roomId}] Disconnected: {reason}");

            this.Invoke((MethodInvoker)delegate
            {
                if (client != null)
                {
                    UpdateStatus($"Disconnected: {reason}");
                    DisconnectFromServer();
                }
            });
        }


        // Vẽ bàn cờ


        private void BoardPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            for (int i = 0; i <= Common.BOARD_SIZE; i++)
            {
                g.DrawLine(Pens.Black, 0, i * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE, i * Common.CELL_SIZE);
                g.DrawLine(Pens.Black, i * Common.CELL_SIZE, 0, i * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE);
            }

            for (int row = 0; row < Common.BOARD_SIZE; row++)
            {
                for (int col = 0; col < Common.BOARD_SIZE; col++)
                {
                    int x = col * Common.CELL_SIZE;
                    int y = row * Common.CELL_SIZE;

                    if (board[row, col] == Common.CellState.X)
                    {
                        g.DrawLine(new Pen(Color.Blue, 2), x + 5, y + 5, x + Common.CELL_SIZE - 5, y + Common.CELL_SIZE - 5);
                        g.DrawLine(new Pen(Color.Blue, 2), x + Common.CELL_SIZE - 5, y + 5, x + 5, y + Common.CELL_SIZE - 5);
                    }
                    else if (board[row, col] == Common.CellState.O)
                    {
                        g.DrawEllipse(new Pen(Color.Red, 2), x + 5, y + 5, Common.CELL_SIZE - 10, Common.CELL_SIZE - 10);
                    }
                }
            }
        }

        // Xử lý sự kiện nhấn đầu hàng


        private void Surrender_Click(object sender, EventArgs e)
        {
            if (gameStatus == Common.GameStatus.Playing)
            {
                SendMessage(Common.FormatMessage("SURRENDER", ""));
            }
        }

        // Xử lý sự kiện nhấn chuột trên bàn cờ

        private void BoardPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (gameStatus == Common.GameStatus.Playing && isMyTurn)
            {
                int col = e.X / Common.CELL_SIZE;
                int row = e.Y / Common.CELL_SIZE;

                if (row >= 0 && row < Common.BOARD_SIZE && col >= 0 && col < Common.BOARD_SIZE &&
                    board[row, col] == Common.CellState.Empty)
                {
                    SendMessage(Common.FormatMessage("MOVE", $"{row},{col}"));
                }
            }
        }

        // Xử lý sự kiện nhấn nút gửi tin nhắn chat

        private void SendChatMessage()
        {
            string message = chatInput.Text.Trim();
            if (!string.IsNullOrEmpty(message) && client != null && sslStream != null)
            {
                SendMessage(Common.FormatMessage("CHAT", message));
                chatInput.Text = "";
            }
        }

        // Gửi tin nhắn đến server

        private void SendMessage(string message)
        {
            try
            {
                lastSendTime = DateTime.Now;
                byte[] data = Encoding.ASCII.GetBytes(message);
                sslStream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending message: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Thêm tin nhắn vào khung chat

        private void AddChatMessage(string message)
        {
            if (chatBox.InvokeRequired)
            {
                chatBox.Invoke(new Action<string>(AddChatMessage), message);
            }
            else
            {
                chatBox.Items.Add(message);
                chatBox.SelectedIndex = chatBox.Items.Count - 1;
            }
        }

        // Cập nhật trạng thái trên giao diện

        private void UpdateStatus(string status)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action<string>(UpdateStatus), status);
            }
            else
            {
                statusLabel.Text = status;
            }
        }


        private void UpdateRoomLabel(string text)
        {
            if (roomLabel.InvokeRequired)
            {
                roomLabel.Invoke(new Action<string>(UpdateRoomLabel), text);
            }
            else
            {
                roomLabel.Text = text;
            }
        }

        private void UpdateBoard()
        {
            if (boardPanel.InvokeRequired)
            {
                boardPanel.Invoke(new Action(UpdateBoard));
            }
            else
            {
                boardPanel.Invalidate();
            }
        }

        private void InitializeBoard()
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    board[i, j] = Common.CellState.Empty;
                }
            }
        }


        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static void Main()
        {
            // Open console window
            AllocConsole();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CaroClient form1 = new CaroClient();
            CaroClient form2 = new CaroClient();

            form1.Show();
            form2.Show();

            // Now the console will be visible alongside the forms
            Console.WriteLine("Console is now visible alongside the forms!");

            Application.Run();
        }
    }
}
