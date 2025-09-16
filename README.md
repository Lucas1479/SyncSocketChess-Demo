# Synchronous Socket Chess Server Demo

A demonstration project showcasing how to build a multi-player chess game server using **synchronous sockets** without relying on high-level APIs or frameworks. This project implements a thread-per-connection architecture to handle concurrent client connections while maintaining game state through manual synchronization.

## 🎯 Project Overview

This is a **demo project** that demonstrates fundamental networking concepts by implementing a chess game server using only low-level socket programming. The server handles multiple concurrent players, manages game state, and provides a REST-like API interface - all built from scratch without using advanced frameworks like ASP.NET Core, SignalR, or WebSockets.

## 🏗️ Architecture Highlights

### Synchronous Socket Implementation
- **Thread-per-connection model**: Each client connection gets its own dedicated thread
- **Manual HTTP parsing**: Custom HTTP request/response handling without web frameworks
- **Synchronous I/O**: Uses blocking socket operations for simplicity and educational purposes
- **State management**: Manual thread synchronization using locks and shared data structures

### Key Technical Decisions
- **No async/await**: Deliberately uses synchronous patterns to demonstrate fundamental concepts
- **Manual HTTP**: Custom request parsing instead of using HttpListener or ASP.NET
- **Thread synchronization**: Uses `lock` statements and thread-safe collections
- **Keep-alive connections**: Maintains persistent connections for better performance

## 🚀 Backend Implementation Details

### Core Components

#### 1. SynchronousSocketListener Class
```csharp
public class SynchronousSocketListener
{
    // Thread-safe data structures
    private readonly object playerLock = new();
    private readonly HashSet<string> registeredPlayers = new();
    private readonly Queue<string> waitingPlayers = new();
    private readonly Dictionary<string, GameRecord> activeGames = new();
}
```

#### 2. Thread-per-Connection Architecture
```csharp
// Main listening loop
while (true)
{
    Socket handler = listener.Accept();
    Thread subThread = new Thread(() => {
        // Handle client requests in dedicated thread
        while (true)
        {
            // Process HTTP requests synchronously
            int bytesRec = handler.Receive(bytes);
            // Parse and dispatch requests
        }
    });
    subThread.Start();
}
```

#### 3. Manual HTTP Request Processing
- **Custom HTTP parser**: Extracts method, path, and query parameters
- **Request routing**: Directs requests to appropriate handlers
- **Response construction**: Manually builds HTTP responses with proper headers

#### 4. Game State Management
- **Thread-safe operations**: All game state modifications are protected by locks
- **Player pairing**: Automatic matching of waiting players
- **Move tracking**: Stores and retrieves player moves
- **Game lifecycle**: Handles game creation, progress, and termination

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/register` | GET | Generate unique player ID |
| `/pairme?player={username}` | GET | Join waiting queue or pair with opponent |
| `/mymove?player={username}&id={gameId}&move={moveJson}` | GET | Submit player move |
| `/theirmove?player={username}&id={gameId}` | GET | Retrieve opponent's move |
| `/quit?player={username}&id={gameId}` | GET | Terminate game |

## 🔧 Technical Implementation

### Why Synchronous Sockets?

This project deliberately uses synchronous socket programming to demonstrate:

1. **Fundamental concepts**: Understanding how network communication works at the lowest level
2. **Thread management**: Manual thread creation and synchronization
3. **State management**: Building thread-safe data structures without frameworks
4. **HTTP protocol**: Manual parsing and response construction
5. **Educational value**: Clear, readable code that shows the underlying mechanisms

### Thread Safety Strategy

```csharp
// All shared data access is protected
lock (playerLock) 
{
    // Safe access to shared collections
    if (waitingPlayers.Count > 0)
    {
        string waitingPlayer = waitingPlayers.Dequeue();
        // Update game state
    }
}
```

### Connection Management

- **Keep-alive support**: Maintains persistent connections
- **Graceful disconnection**: Handles client disconnections properly
- **Resource cleanup**: Ensures sockets are closed and threads terminated

## 🎮 Frontend Integration

The project includes a simple HTML/JavaScript client that demonstrates:
- **Web-based chess interface**: Visual chess board with drag-and-drop
- **Real-time updates**: Polling-based move synchronization
- **Game state display**: Shows current game status and player information

## 🚀 Getting Started

### Prerequisites
- .NET 8.0 SDK
- Modern web browser (for client)

### Running the Server
```bash
cd server-sli776
dotnet run
```

The server will start listening on `http://localhost:11000`

### Running the Client
Open `ChessClient.html` in your web browser and start playing!

## 📚 Learning Objectives

This demo project serves as an educational tool to understand:

1. **Low-level networking**: How sockets work without abstraction layers
2. **Concurrent programming**: Thread management and synchronization
3. **Protocol implementation**: Manual HTTP request/response handling
4. **State management**: Building thread-safe game state without frameworks
5. **Client-server architecture**: How web applications communicate

## ⚠️ Production Considerations

This is a **demonstration project** and is not intended for production use. For production applications, consider:

- **Async/await patterns**: Better resource utilization
- **Connection pooling**: More efficient connection management
- **Web frameworks**: ASP.NET Core, SignalR, or WebSockets
- **Database persistence**: Proper data storage instead of in-memory collections
- **Security**: Authentication, authorization, and input validation
- **Error handling**: Comprehensive exception management
- **Logging**: Proper logging and monitoring

## 🎓 Educational Value

This project demonstrates that modern web applications can be built using fundamental programming concepts. By avoiding high-level frameworks, developers can gain deeper understanding of:

- Network programming fundamentals
- Thread synchronization techniques
- HTTP protocol implementation
- Client-server communication patterns
- State management in concurrent environments

The synchronous approach makes the code more readable and easier to understand, making it an excellent learning resource for understanding the underlying mechanisms of modern web applications.
