# Card Effects Reference

| Card Name | Observable Effect |
| :--- | :--- |
| `WisdomKingCard` | Draw 1 card. If Day, gain 2 VP per played Location. If Night, gain 1 VP per played Location. |
| `FollyKingCard` | Draw 1 card. If Day, gain 1 VP per played Location. If Night, gain 2 VP per played Location. |
| `OgreCard` | Return 1 played Location and 1 played Woman to your hand. |
| `PrincessCard` | Return 1 played Man to your hand. |
| `EnchantedForestCard` | Draw 1 card. If Day, all players draw 1 card. If Night, all players discard 1 card. |
| `WizardCard` | Reveal the top card of the deck and play it immediately. |
| `YoungHeroCard` | Take 1 Armor or 1 Sword from the supply. |
| `DayCard` | It becomes Day. Discard all played Night cards. |
| `NightCard` | It becomes Night. Discard all played Day cards. |
| `FairyCard` | Take 1 Magic Wand or 1 Sword from the supply. |
| `CharmCard` | Take 1 Item (Armor, Magic Wand, Gold Coin, or Sword) from the supply. |
| `CursedCastleCard` | Take 1 Armor or 1 Gold Coin from the supply. |
| `HiddenCaveCard` | Take 1 Magic Wand or 1 Gold Coin from the supply. |
| `DarkLordCard` | Gain 2 VP per played Villain. Discard 1 played Location. |
| `NightChildCard` | If it is Night, gain 2 VP at the end of your turn. |
| `DragonCard` | Discard 1 played Location and 1 played Man. Gain 2 VP per card discarded this way. |
| `OldMasterCard` | If it is Day, gain 2 VP at the end of your turn. |
| `MerchantCard` | Draw 2 cards. Gain 2 VP per played Merchant. |
| `GypsyCard` | All players reveal their hands. Gain 1 VP per revealed Location. Draw 1 card per revealed Gypsy. |
| `SecretHelperCard` | Look at the top 2 cards of the deck. Take one into your hand, remove the other from the game. You may play 1 extra card this turn. |
| `WitchCard` | While in front of you: you cannot draw from the deck, and you may play 1 extra card each of your turns. Discarded at the start of your turn once your hand is empty or the deck has run out. |


## Global Rules

| Rule | Observable Effect |
| :--- | :--- |
| `Rule A1` | 1) Discard 1 Armor to take the top card of the discard pile. 2) Discard 1 Sword and 1 played Villain to gain 3 VP. 3) Discard 1 Magic Wand to draw 1 card, take 1 Gold Coin from the supply, and gain 1 VP. |
| `Rule A2` | 1) Discard 1 Magic Wand to play 1 extra card this turn. 2) Discard 1 Sword and 1 played Villain to take 1 Gold Coin from the supply and gain 1 VP. |
| `Rule A3` | 1) Discard 1 Armor and 1 Sword to draw 2 cards and gain 3 VP. 2) Discard 1 Magic Wand and 1 played Villain to take 1 Gold Coin from the supply and gain 1 VP. |
| `Rule B2` | Gain 12 VP for each set of 3 Gold Coins and 1 Magic Wand you have. |

One in-game rule (`A*`) and one end-of-game rule (`B*`) are drawn at random per game
by the server in `Room.StartGame`, and their ids travel to clients in `GameStartedEvent`.
Pinning a specific rule for testing: set its id on `GameConnectionManager` instead of `0`.