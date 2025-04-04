using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Net.Security;
using System.Security.Authentication;

namespace CaroServer
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

    public class Room
    {
        public List<SslStream> Players { get; set; } = new List<SslStream>();
        public Common.CellState[,] Board { get; set; } = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        public Common.GameStatus GameStatus { get; set; } = Common.GameStatus.Waiting;
        public int CurrentPlayerIndex { get; set; } = 0;
        public int RoomId { get; set; }

        public Room(int id)
        {
            RoomId = id;
            InitializeBoard();
        }

        public void InitializeBoard()
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                    Board[i, j] = Common.CellState.Empty;
        }
    }

    public class Server
    {
        private TcpListener server;
        private List<Room> rooms = new List<Room>();
        private Action<string> updateStatusCallback;
        private X509Certificate2 serverCertificate;

        public Server(Action<string> updateStatusCallback)
        {
            this.updateStatusCallback = updateStatusCallback;

            string certPath = @"E:\LTM\BTL(tmp)\LTM_Nhom17\CaroServer\server.pfx";
            string certPassword = "Kiet.132003";
            serverCertificate = new X509Certificate2(certPath, certPassword);
        }

        public void Start()
        {
            try
            {
                server = new TcpListener(IPAddress.Any, 8888);
                server.Start();
                updateStatusCallback?.Invoke("Server Status: Running on port 8888");

                Thread listenerThread = new Thread(ListenForClients);
                listenerThread.IsBackground = true;
                listenerThread.Start();
            }
            catch (Exception ex)
            {
                updateStatusCallback?.Invoke($"Error starting server: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (server != null)
            {
                server.Stop();
                server = null;

                rooms.Clear();
                updateStatusCallback?.Invoke("Server Status: Not Running");
            }
        }

        private void ListenForClients()
        {
            try
            {
                while (true)
                {
                    TcpClient client = server.AcceptTcpClient();

                    Thread sslThread = new Thread(() =>
                    {
                        SslStream sslStream = new SslStream(client.GetStream(), false);
                        try
                        {
                            sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls12, false);
                            AssignClientToRoom(sslStream);
                        }
                        catch (Exception ex)
                        {
                            updateStatusCallback?.Invoke($"SSL Auth failed: {ex.Message}");
                            sslStream.Close();
                        }
                    });

                    sslThread.IsBackground = true;
                    sslThread.Start();
                }
            }
            catch (Exception ex)
            {
                updateStatusCallback?.Invoke($"Server error: {ex.Message}");
            }
        }

        private void AssignClientToRoom(SslStream sslStream)
        {
            Room availableRoom = null;

            foreach (var room in rooms)
            {
                if (room.Players.Count < 2 && room.GameStatus == Common.GameStatus.Waiting)
                {
                    availableRoom = room;
                    break;
                }
            }

            if (availableRoom == null)
            {
                availableRoom = new Room(rooms.Count);
                rooms.Add(availableRoom);
            }

            int playerIndex = availableRoom.Players.Count;
            availableRoom.Players.Add(sslStream);

            updateStatusCallback?.Invoke($"Player joined Room {availableRoom.RoomId}. Players: {availableRoom.Players.Count}/2");

            Thread clientThread = new Thread(() => HandleClient(sslStream, availableRoom, playerIndex));
            clientThread.IsBackground = true;
            clientThread.Start();

            string role = (playerIndex == 0) ? "X" : "O";
            SendMessage(sslStream, Common.FormatMessage("ROLE", $"{role},{availableRoom.RoomId}"));

            if (availableRoom.Players.Count == 2)
            {
                availableRoom.GameStatus = Common.GameStatus.Playing;
                BroadcastToRoom(availableRoom, Common.FormatMessage("START", ""));
                updateStatusCallback?.Invoke($"Room {availableRoom.RoomId}: Game started. Player X's turn.");
            }
        }

        private void HandleClient(SslStream sslStream, Room room, int playerIndex)
        {
            byte[] buffer = new byte[1024];

            try
            {
                while (true)
                {
                    int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Common.ParseMessage(message, out string command, out string data);

                    if (command == "MOVE" && room.GameStatus == Common.GameStatus.Playing && room.CurrentPlayerIndex == playerIndex)
                    {
                        string[] coords = data.Split(',');
                        int row = int.Parse(coords[0]);
                        int col = int.Parse(coords[1]);

                        if (row >= 0 && row < Common.BOARD_SIZE && col >= 0 && col < Common.BOARD_SIZE &&
                            room.Board[row, col] == Common.CellState.Empty)
                        {
                            room.Board[row, col] = (playerIndex == 0) ? Common.CellState.X : Common.CellState.O;
                            BroadcastToRoom(room, Common.FormatMessage("MOVE", $"{row},{col},{playerIndex}"));

                            if (CheckWin(room, row, col))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", $"Player {((playerIndex == 0) ? "X" : "O")} wins!"));
                                updateStatusCallback?.Invoke($"Room {room.RoomId}: Player {((playerIndex == 0) ? "X" : "O")} wins!");
                            }
                            else if (IsBoardFull(room))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", "Draw!"));
                                updateStatusCallback?.Invoke($"Room {room.RoomId}: Draw!");
                            }
                            else
                            {
                                room.CurrentPlayerIndex = 1 - room.CurrentPlayerIndex;
                                updateStatusCallback?.Invoke($"Room {room.RoomId}: Player {((room.CurrentPlayerIndex == 0) ? "X" : "O")}'s turn");
                            }
                        }
                    }
                    else if (command == "CHAT")
                    {
                        BroadcastToRoom(room, Common.FormatMessage("CHAT", $"Player {playerIndex + 1}: {data}"));
                    }
                    else if (command == "RESTART" && room.GameStatus == Common.GameStatus.GameOver)
                    {
                        room.InitializeBoard();
                        room.GameStatus = Common.GameStatus.Playing;
                        room.CurrentPlayerIndex = 0;
                        BroadcastToRoom(room, Common.FormatMessage("RESTART", ""));
                        updateStatusCallback?.Invoke($"Room {room.RoomId}: Game restarted. Player X's turn.");
                    }
                }
            }
            catch
            {
                if (room.Players.Contains(sslStream))
                {
                    room.Players.Remove(sslStream);
                    updateStatusCallback?.Invoke($"Room {room.RoomId}: Player disconnected. {room.Players.Count}/2 players.");
                    BroadcastToRoom(room, Common.FormatMessage("DISCONNECT", $"Player {playerIndex + 1} disconnected"));
                    if (room.GameStatus == Common.GameStatus.Playing)
                        room.GameStatus = Common.GameStatus.Waiting;
                }

                sslStream.Close();
            }
        }

        private void BroadcastToRoom(Room room, string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (var ssl in room.Players)
            {
                try { ssl.Write(data, 0, data.Length); } catch { }
            }
        }

        private void SendMessage(SslStream ssl, string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            ssl.Write(data, 0, data.Length);
        }

        private bool CheckWin(Room room, int row, int col)
        {
            Common.CellState player = room.Board[row, col];
            int count;

            // Horizontal
            count = 0;
            for (int c = Math.Max(0, col - 4); c <= Math.Min(Common.BOARD_SIZE - 1, col + 4); c++)
                count = (room.Board[row, c] == player) ? count + 1 : 0;
            if (count >= Common.WINNING_COUNT) return true;

            // Vertical
            count = 0;
            for (int r = Math.Max(0, row - 4); r <= Math.Min(Common.BOARD_SIZE - 1, row + 4); r++)
                count = (room.Board[r, col] == player) ? count + 1 : 0;
            if (count >= Common.WINNING_COUNT) return true;

            // Diagonal TL-BR
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col + i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                    count = (room.Board[r, c] == player) ? count + 1 : 0;
                if (count >= Common.WINNING_COUNT) return true;
            }

            // Diagonal TR-BL
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col - i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                    count = (room.Board[r, c] == player) ? count + 1 : 0;
                if (count >= Common.WINNING_COUNT) return true;
            }

            return false;
        }

        private bool IsBoardFull(Room room)
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                    if (room.Board[i, j] == Common.CellState.Empty) return false;

            return true;
        }
    }
}
