using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CaRoServer
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
        public List<TcpClient> Players { get; set; } = new List<TcpClient>();
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
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    Board[i, j] = Common.CellState.Empty;
                }
            }
        }
    }

    public class Server
    {
        private TcpListener server;
        private List<Room> rooms = new List<Room>();
        private Action<string> updateStatusCallback;

        public Server(Action<string> updateStatusCallback)
        {
            this.updateStatusCallback = updateStatusCallback;
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

                foreach (var room in rooms)
                {
                    foreach (var client in room.Players)
                    {
                        try { client.Close(); } catch { }
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
                    AssignClientToRoom(client);
                }
            }
            catch (SocketException)
            {
                // Server stopped
            }
            catch (Exception ex)
            {
                updateStatusCallback?.Invoke($"Server error: {ex.Message}");
            }
        }

        private void AssignClientToRoom(TcpClient client)
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
            availableRoom.Players.Add(client);

            updateStatusCallback?.Invoke($"Player joined Room {availableRoom.RoomId}. Players: {availableRoom.Players.Count}/2");

            Thread clientThread = new Thread(() => HandleClient(client, availableRoom, playerIndex));
            clientThread.IsBackground = true;
            clientThread.Start();
        }

        private void HandleClient(TcpClient client, Room room, int playerIndex)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try
            {
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    Common.ParseMessage(message, out string command, out string data);

                    if (command == "CHAT")
                    {
                        BroadcastToRoom(room, Common.FormatMessage("CHAT", $"Player {playerIndex + 1}: {data}"));
                    }
                }
            }
            catch (Exception)
            {
                room.Players.Remove(client);
                updateStatusCallback?.Invoke($"Room {room.RoomId}: Player disconnected.");
                try { client.Close(); } catch { }
            }
        }

        private void BroadcastToRoom(Room room, string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            foreach (var client in room.Players)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(data, 0, data.Length);
                }
                catch { }
            }
        }
    }
}
