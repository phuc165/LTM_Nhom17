using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
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
        private NetworkStream stream;
        private Thread listenThread;
        private Panel boardPanel;
        private Label statusLabel;
        private Label roomLabel;
        private TextBox chatInput;
        private Button sendButton;
        private ListBox chatBox;
        private Button connectButton;
        private TextBox serverInput;
        private Common.CellState[,] board = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        private Common.GameStatus gameStatus = Common.GameStatus.Waiting;
        private Common.CellState playerRole = Common.CellState.Empty;
        private bool isMyTurn = false;
        private int roomId = -1;

        public CaroClient()
        {
            InitializeComponents();
            InitializeBoard();
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
                Location = new Point(20, 20),
                AutoSize = true
            };

            serverInput = new TextBox
            {
                Text = "127.0.0.1",
                Location = new Point(120, 20),
                Size = new Size(150, 20)
            };

            connectButton = new Button
            {
                Text = "Connect",
                Location = new Point(280, 19),
                Size = new Size(80, 23)
            };

            statusLabel = new Label
            {
                Text = "Not connected",
                Location = new Point(20, 50),
                Size = new Size(500, 20)
            };

            roomLabel = new Label
            {
                Text = "Room: None",
                Location = new Point(20, 70),
                Size = new Size(200, 20)
            };

            // Game board
            boardPanel = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(Common.BOARD_SIZE * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE),
                BorderStyle = BorderStyle.FixedSingle
            };

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

            sendButton = new Button
            {
                Text = "Send",
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 230, 509),
                Size = new Size(70, 23)
            };

            this.Controls.Add(serverLabel);
            this.Controls.Add(serverInput);
            this.Controls.Add(connectButton);
            this.Controls.Add(statusLabel);
            this.Controls.Add(roomLabel);
            this.Controls.Add(boardPanel);
            this.Controls.Add(chatBox);
            this.Controls.Add(chatInput);
            this.Controls.Add(sendButton);

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

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CaroClient form1 = new CaroClient();
            CaroClient form2 = new CaroClient();

            form1.Show();
            form2.Show();

            Application.Run();
        }
    }
}
