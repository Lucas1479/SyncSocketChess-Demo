using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SocketTest
{
   public class SynchronousSocketListener
   {
      // Game state data structure
      private class GameRecord
      {
         public string GameId;
         public string Player1;
         public string Player2;
         public string State; // "wait", "progress"
         public string? LastMoveP1;
         public string? LastMoveP2;
      }
      
      // Shared data structures with thread synchronization
      private readonly object playerLock = new();
      private readonly HashSet<string> registeredPlayers = new();
      private readonly Queue<string> waitingPlayers = new(); 
      private readonly Dictionary<string, GameRecord> activeGames = new(); // gameId -> record

      // Main server method - implements thread-per-connection architecture
      public void StartListening()
      {
         const int BACKLOG = 10; // maximum length of pending connections queue
         const int DEFPORTNUM = 11000;
         // Establish the local endpoint for the socket.  
         // Dns.GetHostName returns the name of the
         // host running the application.  
         //IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
         IPAddress ipAddress = new([127, 0, 0, 1]); //ipHostInfo.AddressList[0];
         IPEndPoint localEndPoint = new(ipAddress, DEFPORTNUM);

         // Create a TCP/IP socket.  
         Socket listener = new(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

         // Bind the socket to the local endpoint and
         // listen for incoming connections.  
            try
            {
               listener.Bind(localEndPoint);
               listener.Listen(BACKLOG);
               Console.WriteLine($"Listening at {localEndPoint}");

               // Start listening for connections.  
               while (true)
               {
                  Console.WriteLine($"Waiting for a connection at {localEndPoint.Port}...");
                  // Program is suspended while waiting for an incoming connection.  
                  Socket handler = listener.Accept();
                  
                  // Create dedicated thread for each connection
                  Thread subThread = new Thread(() => {
                     var remoteEndPoint = (handler.RemoteEndPoint as IPEndPoint)!;
                     Console.WriteLine($"Connection established with {remoteEndPoint}"); 
                     try {
                        // Handle multiple requests from same client (keep-alive)
                        while (true)
                        {

                           // Receive buffer for incoming data.  
                           byte[] bytes = new byte[1024];
                           // Incoming data from the client as a string.
                           string data = null!;

                           // An incoming connection needs to be processed.
                           int bytesRec = 0;
                           // Accumulate fragmented HTTP data until complete
                           while (true) {
                              bytesRec = handler.Receive(bytes);
                              // Check for client disconnection
                              if (bytesRec == 0) {
                                 return; 
                              }
                              data += Encoding.ASCII.GetString(bytes, 0, bytesRec);
                              if (data.IndexOf('\n') > -1) {
                                 break;
                              }
                           }

                           HttpRequestInfo requestInfo = ParseRawHttpRequest(data);
                           DispatchRequest(requestInfo,handler,remoteEndPoint);
                           
                        }  
                  } catch (Exception ex) {
                     Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} error: {ex.Message}");
                  }
                  finally { 
                     Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} closing connection with {remoteEndPoint} and terminating");
                     handler.Close();
                  }

               });
               
               subThread.Start();
               
            }
         }
         catch (Exception e)
         {
            Console.WriteLine(e.ToString());
         }

         Console.WriteLine("\nPress ENTER to continue...");
         Console.Read();
      }

      // Manual HTTP request parser - extracts method, path, and query parameters
      private HttpRequestInfo ParseRawHttpRequest(string rawRequest){
         HttpRequestInfo info = new();
         
         string[] lines = rawRequest.Split("\r\n");  
         string[] requestLine = lines[0].Split(' ');
         info.Method = requestLine[0];
         string url = requestLine[1];
         
         string[] urlParts = url.Split('?', 2);
         info.Path = urlParts[0];
         
         if (urlParts.Length > 1)
         {
            string[] paramPairs = urlParts[1].Split('&');
            foreach (var pair in paramPairs)
            {
               var keyValue = pair.Split('=');
               if (keyValue.Length == 2)
                  info.QueryParams[keyValue[0]] = keyValue[1];
            }
         }
         
         return info;
      }

      // Request router - matches paths and directs to appropriate handlers
      private void DispatchRequest(HttpRequestInfo reqInfo,Socket clientHandler, IPEndPoint remoteEndPoint) {
         string fullUrl = reqInfo.Path;
         if (reqInfo.QueryParams.Count > 0) {
            var queryString = string.Join("&", reqInfo.QueryParams.Select(kv => $"{kv.Key}={kv.Value}"));
            fullUrl += "?" + queryString;
         }
         try
         {
            if (reqInfo.Path == "/register") {
               HandleRegisterRequest(clientHandler,remoteEndPoint,fullUrl);
            
            }else if (reqInfo.Path == "/pairme") {
               HandlePairRequest(reqInfo,clientHandler,remoteEndPoint,fullUrl);

            }else if (reqInfo.Path == "/mymove") {
               HandleMyMoveRequest(reqInfo,clientHandler,remoteEndPoint,fullUrl);
            
            }else if (reqInfo.Path == "/theirmove") {
               HandleTheirMoveRequest(reqInfo,clientHandler,remoteEndPoint,fullUrl);
            
            }else if (reqInfo.Path == "/quit") {
               HandleQuitRequest(reqInfo,clientHandler,remoteEndPoint,fullUrl);
            
            }
            else
            {
               SendHttpResponse(clientHandler, 404, "Not Found", "application/json",
                  JsonSerializer.Serialize(new { message = "Invalid endpoint." }), remoteEndPoint, reqInfo.Path);
            }
         }catch(Exception ex)
         {
            Console.WriteLine($"[DispatchError] {ex.Message}");
            SendHttpResponse(clientHandler, 500, "Internal Server Error", "application/json",
               JsonSerializer.Serialize(new { message = "Server error occurred." }), remoteEndPoint, reqInfo.Path);
         }
      }

      // Generate random player ID and register player
      private void HandleRegisterRequest(Socket clientHandler, IPEndPoint remoteEndPoint,string fullUrl="") {
         string newPlayerId = "user_" + Guid.NewGuid().ToString("N")[..8];
         lock (registeredPlayers)
         {
            registeredPlayers.Add(newPlayerId);
         }
         string id = JsonSerializer.Serialize(new { playerId = newPlayerId });
         SendHttpResponse(
            clientHandler,
            200,
            "OK",
            "application/json",
            id,
            remoteEndPoint, 
            fullUrl
         );
         
      }

      // Handle player pairing and game state management
      private void HandlePairRequest(HttpRequestInfo reqInfo, Socket clientHandler, IPEndPoint remoteEndPoint, string fullUrl) 
      {
          if (!reqInfo.QueryParams.TryGetValue("player", out string? username) || string.IsNullOrWhiteSpace(username))
          {
              string message = JsonSerializer.Serialize(new { message = "Please enter a username." });
              SendHttpResponse(clientHandler, 400, "Bad Request", "application/json", message, remoteEndPoint, fullUrl);
              return;
          }

          lock (playerLock) 
          {
              // Check if player already has an active game
              var existingGame = activeGames.Values.FirstOrDefault(g => 
                  g.Player1 == username || g.Player2 == username);
                  
              if (existingGame != null) 
              {
                  var existingResponse = new {
                      gameId = existingGame.GameId,
                      state = existingGame.State,
                      player1 = existingGame.Player1,
                      player2 = existingGame.Player2 ?? "",
                      lastMove1 = existingGame.LastMoveP1 ?? "",
                      lastMove2 = existingGame.LastMoveP2 ?? ""
                  };
                  
                  SendHttpResponse(clientHandler, 200, "OK", "application/json", 
                      JsonSerializer.Serialize(existingResponse), remoteEndPoint, fullUrl);
                  return;
              }
              
              // Pair with waiting player if available
              if (waitingPlayers.Count > 0)
              {
                  string waitingPlayer = waitingPlayers.Dequeue();
                  
                  var waitingGame = activeGames.Values.FirstOrDefault(g => 
                      g.Player1 == waitingPlayer && g.State == "wait");
                      
                  if (waitingGame != null)
                  {
                      waitingGame.Player2 = username;
                      waitingGame.State = "progress";
                      
                      var pairResponse = new {
                          gameId = waitingGame.GameId,
                          state = "progress",
                          player1 = waitingGame.Player1,
                          player2 = username,
                          lastMove1 = "",
                          lastMove2 = ""
                      };
                      
                      SendHttpResponse(clientHandler, 200, "OK", "application/json", 
                          JsonSerializer.Serialize(pairResponse), remoteEndPoint, fullUrl);
                  }
                  else
                  {
                      string gameId = Guid.NewGuid().ToString();
                      
                      activeGames[gameId] = new GameRecord
                      {
                          GameId = gameId,
                          Player1 = waitingPlayer,
                          Player2 = username,
                          State = "progress",
                          LastMoveP1 = "",
                          LastMoveP2 = ""
                      };
                      
                      var newPairResponse = new {
                          gameId = gameId,
                          state = "progress",
                          player1 = waitingPlayer,
                          player2 = username,
                          lastMove1 = "",
                          lastMove2 = ""
                      };
                      
                      SendHttpResponse(clientHandler, 200, "OK", "application/json", 
                          JsonSerializer.Serialize(newPairResponse), remoteEndPoint, fullUrl);
                  }
              }
              else 
              {
                  // No waiting players, add to queue
                  waitingPlayers.Enqueue(username);
                  
                  string gameId = Guid.NewGuid().ToString();
                  
                  activeGames[gameId] = new GameRecord 
                  {
                      GameId = gameId,
                      Player1 = username,
                      Player2 = null,
                      State = "wait",
                      LastMoveP1 = "",
                      LastMoveP2 = ""
                  };
                  
                  var waitResponse = new {
                      gameId = gameId,
                      state = "wait",
                      player1 = username,
                      player2 = "",
                      lastMove1 = "",
                      lastMove2 = ""
                  };
                  
                  SendHttpResponse(clientHandler, 200, "OK", "application/json", 
                      JsonSerializer.Serialize(waitResponse), remoteEndPoint, fullUrl);
              }
          }
      }
            
      // Store player move in game record
      private void HandleMyMoveRequest(HttpRequestInfo reqInfo, Socket clientHandler, IPEndPoint remoteEndPoint,string fullUrl)
      {
         if (!reqInfo.QueryParams.TryGetValue("player", out var username) ||
             !reqInfo.QueryParams.TryGetValue("id", out var gameId) ||
             !reqInfo.QueryParams.TryGetValue("move", out var moveJson))
         {
            SendHttpResponse(clientHandler, 400, "Bad Request", "application/json",
               JsonSerializer.Serialize(new { message = "Missing player, id, or move." }), remoteEndPoint, fullUrl);
            return;
         }

         lock (playerLock) {
            if (!activeGames.TryGetValue(gameId, out var game)) {
               SendHttpResponse(clientHandler, 404, "Not Found", "application/json",
                  JsonSerializer.Serialize(new { message = "Game not found." }), remoteEndPoint, fullUrl);
               return;
            }

            if (game.Player1 != username && game.Player2 != username) {
               SendHttpResponse(clientHandler, 403, "Forbidden", "application/json",
                  JsonSerializer.Serialize(new { message = "You are not part of this game." }), remoteEndPoint, fullUrl);
               return;
            }

            // Store move for appropriate player
            if (game.Player1 == username) {
               game.LastMoveP1 = moveJson;
            }else {
               game.LastMoveP2 = moveJson;
            }
            
            SendHttpResponse(clientHandler, 200, "OK", "application/json",
               JsonSerializer.Serialize(new { message = "Move accepted." }), remoteEndPoint, fullUrl);
         }
      }
      
      // Retrieve opponent's move or notify if game ended
      private void HandleTheirMoveRequest(HttpRequestInfo reqInfo, Socket clientHandler, IPEndPoint remoteEndPoint,string fullUrl)
      {
         if (!reqInfo.QueryParams.TryGetValue("player", out var username) ||
             !reqInfo.QueryParams.TryGetValue("id", out var gameId))
         {
            SendHttpResponse(clientHandler, 400, "Bad Request", "application/json",
               JsonSerializer.Serialize(new { message = "Missing player or id." }), remoteEndPoint, fullUrl);
            return;
         }

         lock (playerLock) {
            // Check if game still exists (opponent might have quit)
            if (!activeGames.TryGetValue(gameId, out var game))
            {
               SendHttpResponse(clientHandler, 410, "Gone", "application/json",
                  JsonSerializer.Serialize(new { message = "Game has been terminated by opponent.", gameEnded = true }), remoteEndPoint, fullUrl);
               return;
            }

            if (game.Player1 != username && game.Player2 != username)
            {
               SendHttpResponse(clientHandler, 403, "Forbidden", "application/json",
                  JsonSerializer.Serialize(new { message = "You are not part of this game." }), remoteEndPoint, fullUrl);
               return;
            }

            string? move = null;

            // Get opponent's last move
            if (username == game.Player1)
            {
               move = game.LastMoveP2;
            }
            else if (username == game.Player2)
            {
               move = game.LastMoveP1;
            }

            var response = new
            {
               move = move ?? ""
            };

            SendHttpResponse(clientHandler, 200, "OK", "application/json", JsonSerializer.Serialize(response), remoteEndPoint, fullUrl);
         }
      }
      
      // Handle game termination and cleanup
      private void HandleQuitRequest(HttpRequestInfo reqInfo, Socket clientHandler, IPEndPoint remoteEndPoint,string fullUrl)
      {
         if (!reqInfo.QueryParams.TryGetValue("player", out var username) ||
             !reqInfo.QueryParams.TryGetValue("id", out var gameId))
         {
            SendHttpResponse(clientHandler, 400, "Bad Request", "application/json",
               JsonSerializer.Serialize(new { message = "Missing player or game ID." }), remoteEndPoint, fullUrl);
            return;
         }

         lock (playerLock)
         {
            if (!activeGames.TryGetValue(gameId, out var game))
            {
               SendHttpResponse(clientHandler, 404, "Not Found", "application/json",
                  JsonSerializer.Serialize(new { message = "Game not found." }), remoteEndPoint, fullUrl);
               return;
            }

            if (game.Player1 != username && game.Player2 != username)
            {
               SendHttpResponse(clientHandler, 403, "Forbidden", "application/json",
                  JsonSerializer.Serialize(new { message = "You are not part of this game." }), remoteEndPoint, fullUrl);
               return;
            }

            // Remove game record
            activeGames.Remove(gameId);

            // Clean up waiting queue if necessary
            if (waitingPlayers.Contains(username))
            {
               var newQueue = new Queue<string>(waitingPlayers.Where(p => p != username));
               waitingPlayers.Clear();
               foreach (var p in newQueue)
                  waitingPlayers.Enqueue(p);
            }

            SendHttpResponse(clientHandler, 200, "OK", "application/json",
               JsonSerializer.Serialize(new { message = "Game has been terminated." }), remoteEndPoint, fullUrl);
         }
         
      }

      // Manual HTTP response construction with CORS and keep-alive support
      private void SendHttpResponse(Socket clientSocket, int statusCode, string statusText, string contentType, string body, IPEndPoint remoteEndPoint,string fullUrl)
      {
         string response = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                           $"Content-Type: {contentType}\r\n" +
                           $"Access-Control-Allow-Origin: *\r\n" +
                           $"Connection: keep-alive\r\n" + 
                           $"Content-Length: {Encoding.ASCII.GetByteCount(body)}\r\n" +
                           "\r\n" +
                           body;

         byte[] responseBytes = Encoding.ASCII.GetBytes(response);
         clientSocket.Send(responseBytes);
         Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} sent response to {remoteEndPoint} for {fullUrl}");
      }
      

      public static int Main(string[] args)
      {
         new SynchronousSocketListener().StartListening();
         return 0;
      }
   }

   // HTTP request data structure
   public class HttpRequestInfo
   {
      public string Method { get; set; } = null!;
      public string Path { get; set; } = null!;
      public Dictionary<string, string> QueryParams { get; set; } = new();

      public HttpRequestInfo()
      {
         
      }
      
      public HttpRequestInfo(string method, string path, Dictionary<string,string> queryParams) {
         this.Method = method;
         this.Path = path;
         this.QueryParams = queryParams;
      }
   }
   
}