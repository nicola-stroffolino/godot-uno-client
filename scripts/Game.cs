using Newtonsoft.Json;
using Godot;
using System;
using Godot.Collections;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public partial class Game : Node2D {
	[Signal] public delegate void GameStartedEventHandler();
	[Signal] public delegate void CardPlacedEventHandler();
	[Signal] public delegate void OppCardsChangedEventHandler();
	[Signal] public delegate void TurnStartedEventHandler();
	[Signal] public delegate void CardDrawnEventHandler();
	private Label _cardColorLbl;
	private Label _cardValueLbl;
	private Label _turnLbl;
	private CardDealer _cardDealer;
	private DrawPile _drawPile;
	private Node _screensContainer;

	public override void _Ready(){
		GetNode<Label>("%Room").Text = PlayerInfo.ConnectedRoomName;
		GetNode<Label>("%Opponent").Text = PlayerInfo.Opponent;
		GetNode<Label>("%Nickname").Text = PlayerInfo.Nickname;
		_cardColorLbl = GetNode<Label>("%CardColor");
		_cardValueLbl = GetNode<Label>("%CardValue");
		_turnLbl = GetNode<Label>("%Turn");
		_cardDealer = GetNode<CardDealer>("%CardDealer");
		_drawPile = GetNode<DrawPile>("%DrawPile");
		_screensContainer = GetNode<Node>("%ScreensContainer");
	}

	public override void _Process(double delta) {
		GameInfo.Ws.Poll();
		if (GameInfo.Ws.GetReadyState() != WebSocketPeer.State.Open) return;

		if (GameInfo.PlayedCard is not null) {
			_cardColorLbl.Text = GameInfo.PlayedCard.GetColorString();
			_cardValueLbl.Text = GameInfo.PlayedCard.Value;
		}

		if (PlayerInfo.Opponent is null) {
			_turnLbl.Text = "WAITING FOR OTHER\nPLAYERS TO CONNECT...";
		} else {
			if (PlayerInfo.IsYourTurn) _turnLbl.Text = "Its your turn!";
			else _turnLbl.Text = "Wait for the other\nplayer's turn...";
		}
			
		/* --- Send Data --- */
		if(GameInfo.Queue.Count != 0) {
			var el = GameInfo.Queue.Dequeue();
			var json = JsonConvert.SerializeObject(el);
			GameInfo.Ws.PutPacket(json.ToUtf8Buffer());
		}

		/* --- Receive Data --- */
		var message = System.Text.Encoding.Default.GetString(GameInfo.Ws.GetPacket());
		var payload = JsonConvert.DeserializeObject<JObject>(message);

		if (payload is null) return;
		GD.Print(payload);

		switch ((string)payload["responseType"]) {
			case "Game Started": 
			{	
				GetNode<Label>("%Opponent").Text = PlayerInfo.Opponent = (string)payload["opponentNickname"];

				GameInfo.DeckCardsNumber = (int)payload["deckCardsNumber"];
				_drawPile.UpdateCardsNumber();

				var discardPileCard = (Card)Scenes.Card.Instantiate();
				discardPileCard.CreateCard((string)payload["discardPile"]);
				_cardDealer.PlayCard(_cardDealer.DrawPile, discardPileCard);

				var cards = payload["cardsDrawn"].ToObject<string[]>();
				_cardDealer.DrawCards(_cardDealer.Hand, cards);

				var oppCards = new string[(int)payload["opponentCardsDrawnNumber"]];
				for (int i = 0; i < (int)payload["opponentCardsDrawnNumber"]; i++) oppCards[i] = "Wild_Back";
				_cardDealer.DrawCards(_cardDealer.OpponentHand, oppCards);

				if (PlayerInfo.IsYourTurn) _cardDealer.CheckCardsAvailability();

				break;
			}
			case "Card Played": {
				if (!PlayerInfo.IsYourTurn) {
					var oppPlayedCard = (Card)_cardDealer.OpponentHand.GetChild(0);
					oppPlayedCard.CreateCard((string)payload["discardPile"]);
					_cardDealer.PlayCard(_cardDealer.OpponentHand, oppPlayedCard);
					_cardDealer.ArrangeCards(_cardDealer.OpponentHand);
				}

				break;
			}
			case "Card Drawn": {
				if (PlayerInfo.IsYourTurn) {
					var drawnCards = payload["drawnCards"].ToObject<string[]>();
					_cardDealer.DrawCards(_cardDealer.Hand, drawnCards);
					_cardDealer.ArrangeCards(_cardDealer.Hand);

					_drawPile.UpdateCardsNumber();
				} else {
					var oppCards = new string[(int)payload["opponentCardsDrawnNumber"]];
					for (int i = 0; i < (int)payload["opponentCardsDrawnNumber"]; i++) {
						oppCards[i] = "Wild_Back";
					}
					_cardDealer.DrawCards(_cardDealer.OpponentHand, oppCards);
					_cardDealer.ArrangeCards(_cardDealer.OpponentHand);
				}

				GameInfo.DeckCardsNumber = (int)payload["deckCardsNumber"];
				
				break;
			}
			case "Card Swapped": {
				var newCards = payload["newCards"].ToObject<string[]>();

				foreach(var card in _cardDealer.OpponentHand.GetChildren().Cast<Card>()) {
					_cardDealer.OpponentHand.RemoveChild(card);
				}

				var oldCards = new string[_cardDealer.Hand.GetChildCount()];
				for (int i = 0; i < oldCards.Length; i++) {
					oldCards[i] = "Wild_Back";
					_cardDealer.Hand.GetChild(i).QueueFree();
				}

				_cardDealer.ManageCards(_cardDealer.Hand, _cardDealer.OpponentHand, oldCards, true);
				_cardDealer.ManageCards(_cardDealer.OpponentHand, _cardDealer.Hand, newCards, true);
				
				break;
			}
			case "Game Ended": {
				var youWon = (bool)payload["winner"];

				var endScreen = (EndScreen)Scenes.EndScreen.Instantiate();
				_screensContainer.AddChild(endScreen);
				endScreen.SetOutcome(youWon);

				break;
			}
			case "Turn Changed": {
				PlayerInfo.IsYourTurn = (bool)payload["yourTurn"];

				break;
			}
			case "Check Cards": {
				_cardDealer.CheckCardsAvailability();
				
				break;
			}
			default: break;
		}
	}

	public override void _ExitTree() {
		GameInfo.Ws.Close();
	}
}


