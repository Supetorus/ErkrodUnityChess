using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityChess.Engine;
using UnityEngine;

public class GameManager : MonoBehaviourSingleton<GameManager> {
	public static event Action NewGameStartedEvent;
	public static event Action GameEndedEvent;
	public static event Action GameResetToHalfMoveEvent;
	public static event Action MoveExecutedEvent;
	
	public Board CurrentBoard {
		get {
			game.BoardTimeline.TryGetCurrent(out Board currentBoard);
			return currentBoard;
		}
	}

	public Side SideToMove {
		get {
			game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
			return currentConditions.SideToMove;
		}
	}

	public Side StartingSide => game.ConditionsTimeline[0].SideToMove;
	public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;
	public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;
	public int FullMoveNumber => StartingSide switch {
		Side.White => LatestHalfMoveIndex / 2 + 1,
		Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
		_ => -1
	};

	private bool isWhiteAI;
	private bool isBlackAI;

	public List<(Square, Piece)> CurrentPieces {
		get {
			currentPiecesBacking.Clear();
			for (int file = 1; file <= 8; file++) {
				for (int rank = 1; rank <= 8; rank++) {
					Piece piece = CurrentBoard[file, rank];
					if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
				}
			}

			return currentPiecesBacking;
		}
	}


	private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();
	
	[SerializeField] private UnityChessDebug unityChessDebug;
	private Game game;
	private FENSerializer fenSerializer;
	private PGNSerializer pgnSerializer;
	private CancellationTokenSource promotionUITaskCancellationTokenSource;
	private ElectedPiece userPromotionChoice = ElectedPiece.None;
	private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
	private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

	private IUCIEngine uciEngine;
	
	public void Start() {
		VisualPiece.VisualPieceMoved += OnPieceMoved;

		serializersByType = new Dictionary<GameSerializationType, IGameSerializer> {
			[GameSerializationType.FEN] = new FENSerializer(),
			[GameSerializationType.PGN] = new PGNSerializer()
		};
		
		//StartNewGame();
		
#if DEBUG_VIEW
		unityChessDebug.gameObject.SetActive(true);
		unityChessDebug.enabled = true;
#endif
	}

	private void OnDestroy() {
		uciEngine?.ShutDown();
	}
	
#if AI_TEST
	public async void StartNewGame(bool isWhiteAI = true, bool isBlackAI = true) {
#else
	public async void StartNewGame(bool isWhiteAI = false, bool isBlackAI = false) {
#endif
		game = new Game();

		this.isWhiteAI = isWhiteAI;
		this.isBlackAI = isBlackAI;

		if (isWhiteAI || isBlackAI) {
			if (uciEngine == null) {
				uciEngine = new MockUCIEngine();
				uciEngine.Start();
			}
			
			await uciEngine.SetupNewGame(game);
			NewGameStartedEvent?.Invoke();

			if (isWhiteAI) {
				Movement bestMove = await uciEngine.GetBestMove(10_000);
				DoAIMove(bestMove);
			}
		} else {
			NewGameStartedEvent?.Invoke();
		}
	}

	public async void StartChess960(bool isWhiteAI = false, bool isBlackAI = false)
	{
		(Square, Piece)[] startingPositionPieces = Generate960();
		game = new Game(GameConditions.NormalStartingConditions, startingPositionPieces);

		this.isWhiteAI = isWhiteAI;
		this.isBlackAI = isBlackAI;

		if (isWhiteAI || isBlackAI)
		{
			if (uciEngine == null)
			{
				uciEngine = new MockUCIEngine();
				uciEngine.Start();
			}

			await uciEngine.SetupNewGame(game);
			NewGameStartedEvent?.Invoke();

			if (isWhiteAI)
			{
				Movement bestMove = await uciEngine.GetBestMove(10_000);
				DoAIMove(bestMove);
			}
		}
		else
		{
			NewGameStartedEvent?.Invoke();
		}
	}

	public (Square, Piece)[] Generate960()
	{
		(Square, Piece)[] layout = new (Square, Piece)[32];

		string[] files = new string[] { "a", "b", "c", "d", "e", "f", "g", "h" };
		List<string> filesList = files.ToList();

		// 0 1 2 3 4 5 6 7
		// place bishops
		string firstBishopFile  = filesList[UnityEngine.Random.Range(0, 4) * 2];
		string secondBishopFile = filesList[UnityEngine.Random.Range(0, 4) * 2 + 1];
		filesList.Remove(firstBishopFile);
		filesList.Remove(secondBishopFile);
		layout[0] = (new Square(firstBishopFile + "1"), new Bishop(Side.White));
		layout[1] = (new Square(firstBishopFile + "8"), new Bishop(Side.Black));
		layout[2] = (new Square(secondBishopFile + "1"), new Bishop(Side.White));
		layout[3] = (new Square(secondBishopFile + "8"), new Bishop(Side.Black));

		// 0 1 2 3 4 5
		// place first rook
		int firstRookIndex = UnityEngine.Random.Range(0, 4); // max value of 4 means there are guaranteed two spots to the right.
		string firstRookFile = filesList[firstRookIndex];
		layout[4] = (new Square(firstRookFile + "1"), new Rook(Side.White));
		layout[5] = (new Square(firstRookFile + "8"), new Rook(Side.Black));

		// 0 1 2 3 4 5
		// place king
		int kingIndex = UnityEngine.Random.Range(firstRookIndex + 1, 5); // will be between first rook and the end, leaving room for other rook.
		string kingFile = filesList[kingIndex];
		layout[6] = (new Square(kingFile + "1"), new King(Side.White));
		layout[7] = (new Square(kingFile + "8"), new King(Side.Black));

		// 0 1 2 3 4 5
		// place second rook
		string secondRookFile = filesList[UnityEngine.Random.Range(kingIndex + 1, 6)];
		filesList.Remove(kingFile); 
		filesList.Remove(firstRookFile); 
		filesList.Remove(secondRookFile);
		layout[8] = (new Square(secondRookFile + "1"), new Rook(Side.White)); 
		layout[9] = (new Square(secondRookFile + "8"), new Rook(Side.Black));

		// 0 1 2
		// place knights
		string firstKnightFile = filesList[UnityEngine.Random.Range(0, 3)];
		filesList.Remove(firstKnightFile);
		layout[10] = (new Square(firstKnightFile + "1"), new Knight(Side.White));
		layout[11] = (new Square(firstKnightFile + "8"), new Knight(Side.Black));
		// 0 1
		int secondKnightIndex = UnityEngine.Random.Range(0, 2);
		string secondKnightFile = filesList[secondKnightIndex];
		filesList.Remove(secondKnightFile);
		layout[12] = (new Square(secondKnightFile + "1"), new Knight(Side.White));
		layout[13] = (new Square(secondKnightFile + "8"), new Knight(Side.Black));

		// 0
		// place queens
		string queenFile = filesList[0];
		layout[14] = (new Square(queenFile + "1"), new Queen(Side.White));
		layout[15] = (new Square(queenFile + "8"), new Queen(Side.Black));

		// place pawns
		int layoutIndex = 16;
		for (int i = 0; i < 8; i++)
		{
			layout[layoutIndex++] = (new Square(files[i] + "2"), new Pawn(Side.White));
			layout[layoutIndex++] = (new Square(files[i] + "7"), new Pawn(Side.Black));
		}

		return layout;
	}

	public string SerializeGame() {
		return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
			? serializer?.Serialize(game)
			: null;
	}
	
	public void LoadGame(string serializedGame) {
		game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
		NewGameStartedEvent?.Invoke();
	}

	public void ResetGameToHalfMoveIndex(int halfMoveIndex) {
		if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;
		
		UIManager.Instance.SetActivePromotionUI(false);
		promotionUITaskCancellationTokenSource?.Cancel();
		GameResetToHalfMoveEvent?.Invoke();
	}

	private bool TryExecuteMove(Movement move) {
		if (!game.TryExecuteMove(move)) {
			return false;
		}

		HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
		if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate) {
			BoardManager.Instance.SetActiveAllPieces(false);
			GameEndedEvent?.Invoke();
		} else {
			BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
		}

		MoveExecutedEvent?.Invoke();

		return true;
	}
	
	private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove) {
		switch (specialMove) {
			case CastlingMove castlingMove:
				BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
				return true;
			case EnPassantMove enPassantMove:
				BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
				return true;
			case PromotionMove { PromotionPiece: null } promotionMove:
				UIManager.Instance.SetActivePromotionUI(true);
				BoardManager.Instance.SetActiveAllPieces(false);

				promotionUITaskCancellationTokenSource?.Cancel();
				promotionUITaskCancellationTokenSource = new CancellationTokenSource();
				
				ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);
				
				UIManager.Instance.SetActivePromotionUI(false);
				BoardManager.Instance.SetActiveAllPieces(true);

				if (promotionUITaskCancellationTokenSource == null
				    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
				) { return false; }

				promotionMove.SetPromotionPiece(
					PromotionUtil.GeneratePromotionPiece(choice, SideToMove)
				);
				BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
				BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
				BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

				promotionUITaskCancellationTokenSource = null;
				return true;
			case PromotionMove promotionMove:
				BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
				BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
				BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);
				
				return true;
			default:
				return false;
		}
	}
	
	private ElectedPiece GetUserPromotionPieceChoice() {
		while (userPromotionChoice == ElectedPiece.None) { }

		ElectedPiece result = userPromotionChoice;
		userPromotionChoice = ElectedPiece.None;
		return result;
	}
	
	public void ElectPiece(ElectedPiece choice) {
		userPromotionChoice = choice;
	}

	private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null) {
		Square endSquare = new Square(closestBoardSquareTransform.name);

		if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move)) {
			movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
			Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
			game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
			UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
			return;
		}

		if (move is PromotionMove promotionMove) {
			promotionMove.SetPromotionPiece(promotionPiece);
		}

		if ((move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
		    && TryExecuteMove(move)
		) {
			if (move is not SpecialMove) { BoardManager.Instance.TryDestroyVisualPiece(move.End); }

			if (move is PromotionMove) {
				movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
			}

			movedPieceTransform.parent = closestBoardSquareTransform;
			movedPieceTransform.position = closestBoardSquareTransform.position;
		}

		bool gameIsOver = game.HalfMoveTimeline.TryGetCurrent(out HalfMove lastHalfMove)
		                  && lastHalfMove.CausedStalemate || lastHalfMove.CausedCheckmate;
		if (!gameIsOver
			&& (SideToMove == Side.White && isWhiteAI
			    || SideToMove == Side.Black && isBlackAI)
		) {
			Movement bestMove = await uciEngine.GetBestMove(10_000);
			DoAIMove(bestMove);
		}
	}

	private void DoAIMove(Movement move) {
		GameObject movedPiece = BoardManager.Instance.GetPieceGOAtPosition(move.Start);
		GameObject endSquareGO = BoardManager.Instance.GetSquareGOByPosition(move.End);
		OnPieceMoved(
			move.Start,
			movedPiece.transform,
			endSquareGO.transform,
			(move as PromotionMove)?.PromotionPiece
		);
	}

	public bool HasLegalMoves(Piece piece) {
		return game.TryGetLegalMovesForPiece(piece, out _);
	}
}