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
        public SslStream[] Players { get; set; } = new SslStream[2];

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
        private string serverPassword;
        private bool isPasswordVerified = false;

        public Server(Action<string> updateStatusCallback, string serverPassword)
        {
            this.updateStatusCallback = updateStatusCallback;
            this.serverPassword = serverPassword;

            string certPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\server.pfx");
            string certPassword = "nhom17";
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
                // Notify clients about the server shutdown
                foreach (var room in rooms)
                {
                    foreach (var client in room.Players)
                    {
                        try
                        {
                            // Send shutdown message to each client using sslStream
                            if (client != null)
                            {
                                SendMessage(client, Common.FormatMessage("STOP_SERVER", "Server is shutting down"));
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log exception if sending message fails
                            Console.WriteLine($"Error sending shutdown message: {ex.Message}");
                        }
                    }
                }

                server.Stop();
                server = null;

                foreach (var room in rooms)
                {
                    foreach (var client in room.Players)
                    {
                        try
                        {
                            client.Close();
                            client.Close();
                        }
                        catch (Exception ex)
                        {
                            // Log exception if client closing fails
                            Console.WriteLine($"Error closing client connection: {ex.Message}");
                        }
                    }
                }

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
                            RequestPassword(sslStream);
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

        private void RequestPassword(SslStream sslStream)
        {
            try
            {
                // Send PASSWORD_REQUIRED command as a separate message
                SendMessage(sslStream, Common.FormatMessage("PASSWORD_REQUIRED", ""));

                byte[] buffer = new byte[1024];
                int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) return;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                Common.ParseMessage(message, out string command, out string data);

                if (command == "PASSWORD" && data == serverPassword)
                {
                    SendMessage(sslStream, Common.FormatMessage("PASSWORD_ACCEPTED", ""));
                    AssignClientToRoom(sslStream);
                }
                else
                {
                    SendMessage(sslStream, Common.FormatMessage("PASSWORD_REJECTED", "Incorrect password"));
                    sslStream.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void AssignClientToRoom(SslStream sslStream)
        {
            Room availableRoom = null;
            int playerIndex = 0;

            lock (rooms)
            {
                availableRoom = rooms.FirstOrDefault(r => r.Players.All(p => p == null));

                // If no empty rooms, check for rooms with exactly 1 player (waiting for opponent)
                if (availableRoom == null)
                {
                    availableRoom = rooms.FirstOrDefault(r =>
                        r.Players.Count(p => p != null) == 1 &&
                        r.GameStatus == Common.GameStatus.Waiting);
                }

                // If no such room, create new
                if (availableRoom == null)
                {
                    availableRoom = new Room(rooms.Count);
                    rooms.Add(availableRoom);
                }

                // Find first available player index (0 or 1)
                playerIndex = availableRoom.Players[0] == null ? 0 : 1;

                // Set the player in the slot
                availableRoom.Players[playerIndex] = sslStream;

                updateStatusCallback?.Invoke($"Player joined Room {availableRoom.RoomId}. Players: {availableRoom.Players.Count(p => p != null)}/2");

                string role = (playerIndex == 0) ? "X" : "O";
                SendMessage(sslStream, Common.FormatMessage("ROLE", $"{role},{availableRoom.RoomId}") + "\n");

                if (availableRoom.Players.Count(p => p != null) == 2)
                {
                    availableRoom.GameStatus = Common.GameStatus.Playing;
                    BroadcastToRoom(availableRoom, Common.FormatMessage("START", "") + "\n");
                    updateStatusCallback?.Invoke($"Room {availableRoom.RoomId}: Game started. Player X's turn.");
                }
            }

            Thread clientThread = new Thread(() => HandleClient(sslStream, availableRoom, playerIndex));
            clientThread.IsBackground = true;
            clientThread.Start();
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
                            BroadcastToRoom(room, Common.FormatMessage("MOVE", $"{row},{col},{playerIndex} \n"));

                            if (CheckWin(room, row, col))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", $"Player {((playerIndex == 0) ? "X" : "O")} wins! \n"));
                                updateStatusCallback?.Invoke($"Room {room.RoomId}: Player {((playerIndex == 0) ? "X" : "O")} wins!");
                            }
                            else if (IsBoardFull(room))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", "Draw! \n"));
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
                        BroadcastToRoom(room, Common.FormatMessage("CHAT", $"Player {playerIndex + 1}: {data} \n"));
                    }
                    else if (command == "SURRENDER")
                    {
                        room.GameStatus = Common.GameStatus.GameOver;
                        string surrenderMsg = $"Player {((playerIndex == 0) ? "X" : "O")} surrendered";
                        BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", surrenderMsg + "\n"));
                        updateStatusCallback?.Invoke($"Room {room.RoomId}: {surrenderMsg}");
                    }

                    else if (command == "RESTART" && room.GameStatus == Common.GameStatus.GameOver)
                    {
                        room.InitializeBoard();
                        room.GameStatus = Common.GameStatus.Playing;
                        room.CurrentPlayerIndex = 0;
                        BroadcastToRoom(room, Common.FormatMessage("RESTART", "" + "\n"));
                        updateStatusCallback?.Invoke($"Room {room.RoomId}: Game restarted. Player X's turn.");
                    }
                    else if (command == "DISCONNECT")
                    {
                        room.Players[playerIndex] = null;
                        room.InitializeBoard();
                        room.GameStatus = Common.GameStatus.Waiting;
                        room.CurrentPlayerIndex = 0;
                        updateStatusCallback?.Invoke($"Room {room.RoomId}: Player disconnected. {room.Players.Count(p => p != null)}/2 players.");

                        // Nếu phòng có một người chơi, gửi thông báo chờ
                        if (room.Players.Count(p => p != null) == 1)
                        {
                            BroadcastToRoom(room, Common.FormatMessage("WAIT_FOR_OPPONENT", "Waiting for opponent...") + "\n");
                        }
                        else
                        {
                            // Nếu có đủ 2 người, khởi động lại trò chơi
                            BroadcastToRoom(room, Common.FormatMessage("RESTART", "") + "\n");
                        }
                    }
                    else if (command == "SURRENDER")
                    {
                        room.GameStatus = Common.GameStatus.GameOver;
                        string surrenderMsg = $"Player {((playerIndex == 0) ? "X" : "O")} surrendered";
                        BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", surrenderMsg + "\n"));

                        // Instead of removing player, set to null and keep room waiting
                        room.Players[playerIndex] = null;
                        room.InitializeBoard();
                        room.GameStatus = Common.GameStatus.Waiting;
                        room.CurrentPlayerIndex = 0;

                        updateStatusCallback?.Invoke($"Room {room.RoomId}: {surrenderMsg}. Waiting for player.");
                    }
                }
            }
            catch
            {
                if (room.Players.Contains(sslStream))
                {
                    int disconnectedIndex = Array.IndexOf(room.Players, sslStream);
                    room.Players[disconnectedIndex] = null;
                    int count = room.Players.Count(p => p != null);

                    updateStatusCallback?.Invoke($"Room {room.RoomId}: Player disconnected. {room.Players.Count(p => p != null)}/2 players.");

                    BroadcastToRoom(room, Common.FormatMessage("DISCONNECT", $"Player {disconnectedIndex + 1} disconnected") + "\n");

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
            count = 1;  // Start count with the current position
            for (int c = col + 1; c < Common.BOARD_SIZE && room.Board[row, c] == player; c++)
                count++;
            for (int c = col - 1; c >= 0 && room.Board[row, c] == player; c--)
                count++;
            if (count >= Common.WINNING_COUNT) return true;

            // Vertical
            count = 1;  // Start count with the current position
            for (int r = row + 1; r < Common.BOARD_SIZE && room.Board[r, col] == player; r++)
                count++;
            for (int r = row - 1; r >= 0 && room.Board[r, col] == player; r--)
                count++;
            if (count >= Common.WINNING_COUNT) return true;

            // Diagonal TL-BR
            count = 1;  // Start count with the current position
            for (int i = 1; i < Common.BOARD_SIZE; i++)
            {
                int r = row + i;
                int c = col + i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE && room.Board[r, c] == player)
                    count++;
                else break;
            }
            for (int i = 1; i < Common.BOARD_SIZE; i++)
            {
                int r = row - i;
                int c = col - i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE && room.Board[r, c] == player)
                    count++;
                else break;
            }
            if (count >= Common.WINNING_COUNT) return true;

            // Diagonal TR-BL
            count = 1;  // Start count with the current position
            for (int i = 1; i < Common.BOARD_SIZE; i++)
            {
                int r = row + i;
                int c = col - i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE && room.Board[r, c] == player)
                    count++;
                else break;
            }
            for (int i = 1; i < Common.BOARD_SIZE; i++)
            {
                int r = row - i;
                int c = col + i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE && room.Board[r, c] == player)
                    count++;
                else break;
            }
            if (count >= Common.WINNING_COUNT) return true;

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