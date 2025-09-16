"use strict";

// Game state variables
let gameState = {
    username: null,        
    customUsername: null,  
    gameId: null,
    state: 'disconnected',
    isPlayer1: false,
    lastMove: null,
    polling: false
};

const SERVER_URL = 'http://localhost:11000';
const POLL_INTERVAL = 2000; // Poll every 2 seconds

// DOM Elements
const statusMessage = document.getElementById('status-message');
const playerInfo = document.getElementById('player-info');
const gameSection = document.getElementById('game-section');
const moveHistorySection = document.getElementById('move-history-section');
const quitButton = document.getElementById('quit-game');
const usernameInput = document.getElementById('username-input');
const customUsernameField = document.getElementById('custom-username');
const startGameButton = document.getElementById('start-game');

// Initialize game
document.addEventListener('DOMContentLoaded', () => {
    const savedUsername = localStorage.getItem('chess-username');
    if (savedUsername) {
        customUsernameField.value = savedUsername;
    }
    
    customUsernameField.focus();
    
    customUsernameField.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            startGameFlow();
        }
    });
});

// Server API functions
async function registerPlayer() {
    try {
        const response = await fetch(`${SERVER_URL}/register`);
        const data = await response.json();
        gameState.username = data.playerId;
        
        document.getElementById('username').textContent = gameState.customUsername;
        updateStatus('Registered successfully. Looking for game...');
    } catch (error) {
        throw new Error('Registration failed');
    }
}

async function findGame() {
    try {
        const response = await fetch(`${SERVER_URL}/pairme?player=${gameState.customUsername}`);
        const data = await response.json();
        
        gameState.gameId = data.gameId;
        gameState.state = data.state;
        gameState.isPlayer1 = (data.player1 === gameState.customUsername);
        
        document.getElementById('game-id').textContent = gameState.gameId;
        document.getElementById('player-role').textContent = gameState.isPlayer1 ? 'Player 1 (White)' : 'Player 2 (Black)';
        
        playerInfo.style.display = 'block';
        quitButton.style.display = 'inline-block';
        
        if (gameState.state === 'wait') {
            updateStatus('Waiting for another player to join...');
            startPollingForPlayer();
        } else if (gameState.state === 'progress') {
            updateStatus('Game started! You can move pieces freely.');
            startGame();
        }
    } catch (error) {
        throw new Error('Failed to find game');
    }
}

async function startGameFlow() {
    const customName = customUsernameField.value.trim();
    if (!customName) {
        alert('Please enter a username');
        return;
    }
    
    gameState.customUsername = customName;
    localStorage.setItem('chess-username', customName);
    
    usernameInput.style.display = 'none';
    updateStatus('Connecting to server...');
    
    try {
        await registerPlayer();
        await findGame();
    } catch (error) {
        showError('Failed to connect to game server: ' + error.message);
        usernameInput.style.display = 'block';
    }
}

async function sendMove(moveData) {
    try {
        const moveJson = encodeURIComponent(JSON.stringify(moveData));
        const response = await fetch(`${SERVER_URL}/mymove?player=${gameState.customUsername}&id=${gameState.gameId}&move=${moveJson}`);
        const data = await response.json();
        
        if (response.ok) {
            addMoveToHistory(moveData, true);
        }
        return data;
    } catch (error) {
        console.error('Failed to send move:', error);
    }
}

async function getOpponentMove() {
    try {
        const response = await fetch(`${SERVER_URL}/theirmove?player=${gameState.customUsername}&id=${gameState.gameId}`);
        const data = await response.json();

        if (response.status === 410 || data.gameEnded === true) {
            updateStatus('Game ended: Opponent has quit the game.');
            resetGame();
            return;
        }
        
        if (response.ok && data.move && data.move.trim() !== '') {
            const decodedMove = decodeURIComponent(data.move);
            const currentLastMove = gameState.lastMove ? decodeURIComponent(gameState.lastMove) : '';
            
            if (decodedMove !== currentLastMove) {
                gameState.lastMove = data.move;
                const moveData = JSON.parse(decodedMove);
                applyOpponentMove(moveData);
                addMoveToHistory(moveData, false);
            }
        }
    } catch (error) {
        console.error('Failed to get opponent move:', error);
    }
}

async function quitGame() {
    try {
        if (gameState.gameId) {
            const response = await fetch(`${SERVER_URL}/quit?player=${gameState.customUsername}&id=${gameState.gameId}`);
            if (response.ok) {
                updateStatus('You have quit the game.');
                resetGame();
            }
        }
    } catch (error) {
        console.error('Failed to quit game:', error);
    }
}

// Polling functions
function startPollingForPlayer() {
    if (gameState.polling) return;
    gameState.polling = true;
    
    const pollForPlayer = async () => {
        if (!gameState.polling || gameState.state !== 'wait') return;
        
        try {
            const response = await fetch(`${SERVER_URL}/pairme?player=${gameState.customUsername}`);
            const data = await response.json();
            
            if (data.state === 'progress') {
                gameState.state = 'progress';
                updateStatus('Game started! You can move pieces freely.');
                startGame();
                return;
            }
        } catch (error) {
            console.error('Polling error:', error);
        }
        
        setTimeout(pollForPlayer, POLL_INTERVAL);
    };
    
    setTimeout(pollForPlayer, POLL_INTERVAL);
}

function startPollingForMoves() {
    if (gameState.polling) return;
    gameState.polling = true;
    
    const pollForMoves = async () => {
        if (!gameState.polling || gameState.state !== 'progress') return;
        
        await getOpponentMove();
        setTimeout(pollForMoves, POLL_INTERVAL);
    };
    
    setTimeout(pollForMoves, POLL_INTERVAL);
}

// Game logic functions
function startGame() {
    gameState.polling = false;
    gameSection.style.display = 'flex';
    moveHistorySection.style.display = 'block';
    
    buildBoard();
    startPollingForMoves();
}

function addMoveToHistory(moveData, isYourMove) {
    const historyElement = document.getElementById('move-history');
    const moveEntry = document.createElement('div');
    moveEntry.className = 'move-entry';
    
    const moveText = Array.isArray(moveData) ? 
        moveData.map(m => `${m.piece || 'Piece'}: ${m.from} → ${m.to}`).join(', ') :
        `${moveData.piece || 'Piece'}: ${moveData.from} → ${moveData.to}`;
    
    moveEntry.textContent = `${isYourMove ? 'You' : 'Opponent'}: ${moveText}`;
    historyElement.appendChild(moveEntry);
    historyElement.scrollTop = historyElement.scrollHeight;
}

function applyOpponentMove(moveData) {
    // Handle array of moves (for captures, etc.)
    const moves = Array.isArray(moveData) ? moveData : [moveData];
    
    moves.forEach(move => {
        if (move.to === 'bin1' || move.to === 'bin2') {
            // Piece captured - move to bin
            const fromSquare = document.querySelector(`[data-position="${move.from}"]`);
            if (fromSquare && fromSquare.firstChild) {
                const piece = fromSquare.firstChild;
                fromSquare.innerHTML = '';
                document.getElementById('capture-bin').appendChild(piece);
            }
        } else {
            // Regular move
            const fromSquare = document.querySelector(`[data-position="${move.from}"]`);
            const toSquare = document.querySelector(`[data-position="${move.to}"]`);
            
            if (fromSquare && toSquare && fromSquare.firstChild) {
                const piece = fromSquare.firstChild;
                fromSquare.innerHTML = '';
                toSquare.innerHTML = '';
                toSquare.appendChild(piece);
            }
        }
    });
}

// UI Helper functions
function updateStatus(message) {
    statusMessage.textContent = message;
}

function showError(message) {
    statusMessage.textContent = message;
    statusMessage.style.backgroundColor = '#ffebee';
    statusMessage.style.color = '#c62828';
}

function resetGame() {
    gameState.polling = false;
    gameState.state = 'disconnected';
    gameSection.style.display = 'none';
    moveHistorySection.style.display = 'none';
    playerInfo.style.display = 'none';
    quitButton.style.display = 'none';
    usernameInput.style.display = 'block';
}

// Chess board functions
function createSquare(file, rank, colorClass) {
    const square = document.createElement("div");
    square.className = `square ${colorClass}`;
    square.dataset.position = file + rank;
    square.addEventListener("dragover", e => e.preventDefault());
    square.addEventListener("drop", handleDrop);
    return square;
}

function createLabel(content) {
    const label = document.createElement("div");
    label.className = "label";
    label.textContent = content;
    return label;
}

function createPiece(char) {
    const piece = document.createElement("div");
    piece.textContent = char;
    piece.setAttribute("draggable", "true");
    piece.className = "piece";
    piece.addEventListener("dragstart", handleDragStart);
    piece.addEventListener("dragend", handleDragEnd);
    return piece;
}

function handleDragStart(e) {
    e.dataTransfer.setData("text/plain", e.target.textContent);
    e.dataTransfer.setData("source", e.target.parentElement.dataset.position || "bin");
    e.target.classList.add("dragging");
}

function handleDragEnd(e) {
    e.target.classList.remove("dragging");
}



async function handleDrop(e) {
    const pieceChar = e.dataTransfer.getData("text/plain");
    const source = e.dataTransfer.getData("source");
    const target = e.currentTarget;
    const targetPosition = target.dataset.position;

    // Create move data
    const moveData = {
        piece: pieceChar,
        from: source,
        to: targetPosition
    };

    // Handle capture
    if (target.firstChild) {
        const capturedPiece = target.firstChild;
        const captureMove = {
            piece: capturedPiece.textContent,
            from: targetPosition,
            to: "bin1"
        };
        
        // Send compound move (capture + move)
        await sendMove([captureMove, moveData]);
        
        // Apply move locally
        document.getElementById('capture-bin').appendChild(capturedPiece);
    } else {
        // Send single move
        await sendMove(moveData);
    }

    // Apply move locally
    if (source !== "bin") {
        const sourceSquare = document.querySelector(`[data-position="${source}"]`);
        sourceSquare.innerHTML = "";
    } else {
        const draggedEl = document.querySelector('.piece.dragging');
        if (draggedEl) draggedEl.remove();
    }

    const newPiece = createPiece(pieceChar);
    target.innerHTML = "";
    target.appendChild(newPiece);
}

async function handleBinDrop(e) {
    const bin = document.getElementById("capture-bin");
    const pieceChar = e.dataTransfer.getData("text/plain");
    const source = e.dataTransfer.getData("source");

    // Create move data for capturing
    const moveData = {
        piece: pieceChar,
        from: source,
        to: "bin1"
    };

    await sendMove(moveData);

    // Apply move locally
    if (source !== "bin") {
        const sourceSquare = document.querySelector(`[data-position="${source}"]`);
        sourceSquare.innerHTML = "";
    } else {
        const draggedEl = document.querySelector('.piece.dragging');
        if (draggedEl) draggedEl.remove();
    }

    const capturedPiece = createPiece(pieceChar);
    bin.appendChild(capturedPiece);
}

function buildBoard() {
    const board = document.getElementById("chessboard");
    board.innerHTML = ''; // Clear existing board
    
    const files = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];
    const ranks = [8, 7, 6, 5, 4, 3, 2, 1];
    const initialPieces = {
        1: ['♖', '♘', '♗', '♕', '♔', '♗', '♘', '♖'],
        2: Array(8).fill('♙'),
        7: Array(8).fill('♟'),
        8: ['♜', '♞', '♝', '♛', '♚', '♝', '♞', '♜']
    };

    for (let row = 0; row < 10; row++) {
        for (let col = 0; col < 10; col++) {
            // Corners
            if ((row === 0 || row === 9) && (col === 0 || col === 9)) {
                board.appendChild(document.createElement("div"));
            }
            // File labels top/bottom
            else if (row === 0 || row === 9) {
                board.appendChild(createLabel(files[col - 1]));
            }
            // Rank labels left/right
            else if (col === 0 || col === 9) {
                board.appendChild(createLabel(ranks[row - 1]));
            }
            // Squares
            else {
                const file = files[col - 1];
                const rank = ranks[row - 1];
                const isLight = (row + col) % 2 === 0;
                const square = createSquare(file, rank, isLight ? "light" : "dark");

                // Add piece if one is on initial square
                if (initialPieces[rank] && initialPieces[rank][col - 1]) {
                    const piece = createPiece(initialPieces[rank][col - 1]);
                    square.appendChild(piece);
                }

                board.appendChild(square);
            }
        }
    }
}

// Event listeners
quitButton.addEventListener('click', quitGame);
startGameButton.addEventListener('click', startGameFlow);

// Handle page visibility change to pause/resume polling
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        gameState.polling = false;
    } else if (gameState.state === 'progress') {
        startPollingForMoves();
    } else if (gameState.state === 'wait') {
        startPollingForPlayer();
    }
});

function handleAutoQuit() {
    if (gameState.gameId && (gameState.state === 'progress' || gameState.state === 'wait')) {
        const quitUrl = `${SERVER_URL}/quit?player=${gameState.customUsername}&id=${gameState.gameId}`;
        navigator.sendBeacon(quitUrl);
    }
}

window.addEventListener('beforeunload', handleAutoQuit);
window.addEventListener('unload', handleAutoQuit);


