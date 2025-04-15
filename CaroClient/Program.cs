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
        private const int TIMEOUT_MS = 30000;
        private Label receiveCountdownLabel;
        private Label sendCountdownLabel;
        private System.Windows.Forms.Timer timeoutTimer;
        private DateTime lastReceiveTime;
        private DateTime lastSendTime;
        private bool receiveTimeoutOccurred = false;
        private bool sendTimeoutOccurred = false;

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
                Location = new Point(450, 20),
                AutoSize = true
            };
            receiveCountdownLabel = new Label
            {
                Location = new Point(500, 20),
                ForeColor = Color.Green,
                AutoSize = true,
            };

            Label sendTimeoutTextLabel = new Label
            {
                Text = "Send Timeout:",
                Location = new Point(450, 50),
                AutoSize = true
            };
            sendCountdownLabel = new Label
            {
                Location = new Point(500, 50),
                ForeColor = Color.Green,
                AutoSize = true,
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
            this.Controls.Add(sendTimeoutTextLabel);
            this.Controls.Add(receiveCountdownLabel);
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
                bool shouldCheckSend = false;
                bool shouldCheckReceive = false;

                // Khi game chưa bắt đầu, chỉ đếm receive
                if (gameStatus == Common.GameStatus.Waiting)
                {
                    shouldCheckReceive = true;
                    // Reset lại thời gian đếm nhận khi chuyển sang trạng thái Waiting
                    if (lastReceiveTime == DateTime.MinValue)
                    {
                        lastReceiveTime = DateTime.Now; // Cập nhật lại thời gian ngay khi vào trạng thái Waiting
                    }
                    // Đảm bảo rằng không làm ảnh hưởng đến bộ đếm gửi
                    if (lastSendTime != DateTime.MinValue)
                    {
                        lastSendTime = DateTime.MinValue; // Reset bộ đếm gửi khi chuyển sang Waiting
                    }
                }
                else if (gameStatus == Common.GameStatus.Playing)
                {
                    // Khi game đã bắt đầu, kiểm tra lượt
                    if (isMyTurn)
                    {
                        // Nếu là lượt của người chơi, đếm send
                        shouldCheckSend = true;
                        // Reset lại thời gian đếm gửi khi chuyển sang trạng thái Playing
                        if (lastSendTime == DateTime.MinValue)
                        {
                            lastSendTime = DateTime.Now; // Cập nhật lại thời gian ngay khi vào lượt người chơi
                        }
                        // Đảm bảo rằng không làm ảnh hưởng đến bộ đếm nhận
                        if (lastReceiveTime != DateTime.MinValue)
                        {
                            lastReceiveTime = DateTime.MinValue; // Reset bộ đếm nhận khi chuyển sang lượt người chơi
                        }
                    }
                    else
                    {
                        // Nếu là lượt của đối thủ, đếm receive
                        shouldCheckReceive = true;
                        // Reset lại thời gian đếm nhận khi chuyển sang lượt đối thủ
                        if (lastReceiveTime == DateTime.MinValue)
                        {
                            lastReceiveTime = DateTime.Now; // Cập nhật lại thời gian ngay khi vào lượt đối thủ
                        }
                        // Đảm bảo rằng không làm ảnh hưởng đến bộ đếm gửi
                        if (lastSendTime != DateTime.MinValue)
                        {
                            lastSendTime = DateTime.MinValue; // Reset bộ đếm gửi khi chuyển sang lượt đối thủ
                        }
                    }
                }

                // Kiểm tra thời gian nhận (receive)
                if (shouldCheckReceive)
                {
                    double receiveRemaining = 0;
                    if (lastReceiveTime != DateTime.MinValue)
                    {
                        receiveRemaining = (TIMEOUT_MS - (DateTime.Now - lastReceiveTime).TotalMilliseconds) / 1000;
                    }

                    receiveCountdownLabel.Text = $"Receive: {Math.Max(0, receiveRemaining):0.0}s";
                    receiveCountdownLabel.ForeColor = receiveRemaining < 5 ? Color.Red : Color.Green;


                    // Xử lý khi timeout xảy ra (đối thủ hết giờ hoặc mất kết nối)
                    if (receiveRemaining <= 0 && !receiveTimeoutOccurred)
                    {
                        receiveTimeoutOccurred = true;

                        this.BeginInvoke((MethodInvoker)delegate {
                            string message = "";
                            string title = "";

                            if (gameStatus == Common.GameStatus.Playing && !isMyTurn)
                            {
                                message = "Opponent timed out. You win! Want to play again?";
                                title = "Victory";
                            }
                            else if (gameStatus == Common.GameStatus.Waiting)
                            {
                                message = "No response from server. Try to reconnect?";
                                title = "Connection Timeout";
                            }
                            else
                            {
                                message = "Connection timeout occurred. Try to reconnect?";
                                title = "Timeout";
                            }

                            var result = MessageBox.Show(message, title,
                                                       MessageBoxButtons.YesNo,
                                                       MessageBoxIcon.Warning);

                            if (result == DialogResult.Yes)
                            {
                                DisconnectFromServer();

                                Task.Delay(1000).ContinueWith(t => {
                                    this.Invoke((MethodInvoker)delegate {
                                        try
                                        {
                                            ConnectToServer();
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show($"Unable to reconnect: {ex.Message}", "Error",
                                                          MessageBoxButtons.OK, MessageBoxIcon.Error);
                                        }
                                    });
                                });
                            }
                            else
                            {
                                DisconnectFromServer();
                            }
                        });
                        return;
                    }
                }
                else
                {
                    receiveCountdownLabel.Text = "---";
                }

                // Kiểm tra thời gian gửi (send)
                if (shouldCheckSend)
                {
                    double sendRemaining = 0;
                    if (lastSendTime != DateTime.MinValue)
                    {
                        sendRemaining = (TIMEOUT_MS - (DateTime.Now - lastSendTime).TotalMilliseconds) / 1000;
                    }

                    sendCountdownLabel.Text = $"Send: {Math.Max(0, sendRemaining):0.0}s";
                    sendCountdownLabel.ForeColor = sendRemaining < 5 ? Color.Red : Color.Green;

                    if (sendRemaining <= 0 && !sendTimeoutOccurred)
                    {
                        sendTimeoutOccurred = true;

                        // Hiển thị MessageBox với các lựa chọn
                        var result = MessageBox.Show("Time's up, you lost, do you want to play again?",
                                                   "Timeout",
                                                   MessageBoxButtons.YesNo,
                                                   MessageBoxIcon.Warning);

                        if (result == DialogResult.Yes)
                        {
                            // Disconnect first
                            DisconnectFromServer();

                            // Add small delay before reconnecting
                            Task.Delay(1000).ContinueWith(t => {
                                this.Invoke((MethodInvoker)delegate {
                                    try
                                    {
                                        ConnectToServer();
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show($"Unable to reconnect: {ex.Message}", "Error",
                                                      MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                });
                            });
                        }
                        else
                        {
                            DisconnectFromServer();
                        }
                        return;
                    }
                }

                else
                {
                    sendCountdownLabel.Text = "---";
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
                newClient.ReceiveTimeout = 60000;
                newClient.SendTimeout = 60000;

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
                receiveTimeoutOccurred = false;
                sendTimeoutOccurred = false;

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
                try { sslStream?.Dispose(); } catch { }
                try { client?.Close(); } catch { }
                try { client?.Dispose(); } catch { }

                // Reset state only if form isn't disposed
                if (!this.IsDisposed)
                {
                    ResetClientState();
                    receiveTimeoutOccurred = false;
                    sendTimeoutOccurred = false;
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
            byte[] buffer = new byte[4096];

            try
            {
                while (client != null && client.Connected && !this.IsDisposed)
                {
                    try
                    {
                        int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            // Graceful disconnect
                            HandleDisconnection("Server closed the connection");
                            break;
                        }

                        lastReceiveTime = DateTime.Now;
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        string[] messages = receivedData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string message in messages)
                        {
                            if (this.IsDisposed) return;

                            string command, data;
                            Common.ParseMessage(message, out command, out data);

                            Console.WriteLine($"[Client {roomId}] Received: {command} | {data}");

                            this.BeginInvoke((MethodInvoker)delegate
                            {
                                if (!this.IsDisposed)
                                {
                                    try
                                    {
                                        ProcessServerMessage(command, data);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[Client {roomId}] Error processing message: {ex.Message}");
                                    }
                                }
                            });
                        }
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException socketEx)
                    {
                        HandleDisconnection($"Connection error: {socketEx.Message}");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        HandleDisconnection("Connection was disposed");
                        break;
                    }
                    catch (Exception ex)
                    {
                        HandleDisconnection($"Error reading data: {ex.Message}");
                        break;
                    }
                }
            }
            finally
            {
                if (!this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)DisconnectFromServer);
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

                    case "STOP_SERVER":
                        string disconnectMessage = string.IsNullOrEmpty(data) ? "Disconnected from server" : data;

                        this.Invoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(disconnectMessage, "Server Message",
                                          MessageBoxButtons.OK, MessageBoxIcon.Information);
                            DisconnectFromServer();
                        });
                        break;

                    case "DISCONNECT":
                        DisconnectFromServer();

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

                    case "WAIT_FOR_OPPONENT":
                        InitializeBoard();
                        lastMoveRow = -1;
                        lastMoveCol = -1;
                        gameStatus = Common.GameStatus.Waiting; // Chuyển sang trạng thái chờ
                        isMyTurn = false;
                        lastReceiveTime = DateTime.Now; // Bắt đầu đếm receive
                        lastSendTime = DateTime.MinValue;
                        UpdateBoard(); MessageBox.Show(data, "Waiting for opponent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        UpdateStatus(data); // Hiển thị thông báo trong status bar hoặc label
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


        private void HandleDisconnection(string reason)
        {
            Console.WriteLine($"[Client {roomId}] Disconnected: {reason}");

            this.Invoke((MethodInvoker)delegate
            {
                if (client != null)
                {
                    MessageBox.Show($"Disconnected: {reason}", "Disconnect",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
