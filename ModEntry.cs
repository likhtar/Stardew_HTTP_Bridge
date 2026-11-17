#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Objects;

namespace Stardew_HTTP_Bridge
{
    public class CommandPayload
    {
        public string Action { get; set; } = "";
        public string Target { get; set; } = "";
        public string Id { get; set; } = "";
        public int Amount { get; set; } = 0;
        public int Duration { get; set; } = 0;
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
    }

    public class ModEntry : Mod
    {
        private HttpListener? _listener;
        private readonly ConcurrentQueue<CommandPayload> _commandQueue = new();
        private readonly Random _random = new();
        private int _reverseTicks = 0;
        private bool _reverseActive = false;
        private int _freezeTicks = 0;
        private bool _freezeActive = false;
        private InputButton _origMoveUp;
        private InputButton _origMoveDown;
        private InputButton _origMoveLeft;
        private InputButton _origMoveRight;

        private int _cachedMineDepth = 0;
        private bool _hasSkullKey = false;
        private bool _hasRustyKey = false;

        private int _glitchTicks = 0;
        private Texture2D? _pixelTexture;

        private uint _geodeCacheCounter = uint.MaxValue;
        private Dictionary<string, (string LootText, bool Rare)[]> _geodeLootCache = new();
        private List<Item> _geodeItems = new();
        private bool _geodeListInitialized = false;

        private uint _boxCacheCounter = uint.MaxValue;
        private Dictionary<string, (string LootText, bool Rare)[]> _boxLootCache = new();
        private List<Item> _boxItems = new();
        private bool _boxListInitialized = false;

        private static readonly string[] GeodeIds =
        {
            "(O)535", "(O)536", "(O)537", "(O)749", "(O)116", "(O)791"
        };

        private static readonly string[] BoxIds =
        {
            "(O)MysteryBox", "(O)GoldenMysteryBox"
        };

        private static readonly HashSet<string> RareLootIds = new()
        {
            "(O)74", "(O)72", "(O)386", "(O)384"
        };

        private static readonly (string Id, int Weight, int Min, int Max)[] RandomItemPool =
        {
            ("330", 10, 1, 5),
            ("382", 10, 1, 5),
            ("378", 8, 1, 4),
            ("380", 8, 1, 3),
            ("390", 6, 5, 10),
            ("388", 6, 5, 10),
            ("80", 6, 1, 3),
            ("771", 6, 3, 6),
            ("535", 5, 1, 1),
            ("536", 5, 1, 1),
            ("537", 4, 1, 1),
            ("749", 3, 1, 1),
            ("60", 2, 1, 1),
            ("62", 2, 1, 1),
            ("64", 2, 1, 1),
            ("66", 2, 1, 1),
            ("68", 2, 1, 1),
            ("70", 1, 1, 1),
            ("72", 5, 1, 1),
            ("788", 4, 1, 1),
            ("337", 4, 1, 1),
            ("74", 2, 1, 1)
        };

        private (string Id, int Min, int Max) PickRandomItem()
        {
            int total = RandomItemPool.Sum(entry => entry.Weight);
            int roll = _random.Next(total);
            foreach (var entry in RandomItemPool)
            {
                roll -= entry.Weight;
                if (roll < 0)
                {
                    return (entry.Id, entry.Min, entry.Max);
                }
            }
            return (RandomItemPool[^1].Id, 1, 1);
        }

        private static readonly string[] CreepySounds = { "ghost", "mummyGasp", "owl", "thunder", "secret1" };

        private static readonly (string Type, int MinMineDepth, bool NeedsSkullKey)[] MonsterProgression =
        {
            ("slime", 0, false),
            ("bat", 0, false),
            ("crab", 0, false),
            ("fly", 0, false),
            ("dust", 40, false),
            ("ghost", 51, false),
            ("skeleton", 71, false),
            ("serpent", 0, true),
            ("shadow", 0, true),
        };

        private static readonly string[] SafeWarpLocations =
        {
            "AnimalShop", "ArchaeologyHouse", "Backwoods", "Beach", "Blacksmith",
            "BusStop", "CommunityCenter", "ElliottHouse", "Farm", "FishShop",
            "Forest", "HaleyHouse", "Hospital", "JojaMart", "LeahHouse",
            "ManorHouse", "Mountain", "Saloon", "SamHouse", "ScienceHouse",
            "SeedShop", "Sunroom", "Tent", "Town", "Tunnel", "WizardHouse"
        };

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            StartHttpServer();
        }

        private void StartHttpServer()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:8080/stardew/");
                _listener.Prefixes.Add("http://localhost:8080/stardew/");
                _listener.Start();
                Monitor.Log("HTTP Server started on 127.0.0.1:8080 and localhost:8080", LogLevel.Info);
                _listener.BeginGetContext(OnHttpRequest, null);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to start HTTP Server: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnHttpRequest(IAsyncResult result)
        {
            if (_listener == null || !_listener.IsListening) return;

            try
            {
                HttpListenerContext context = _listener.EndGetContext(result);
                _listener.BeginGetContext(OnHttpRequest, null);

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                if (request.HttpMethod == "POST")
                {
                    using StreamReader reader = new(request.InputStream, request.ContentEncoding);
                    string body = reader.ReadToEnd();

                    var payload = JsonSerializer.Deserialize<CommandPayload>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (payload != null)
                    {
                        _commandQueue.Enqueue(payload);
                    }

                    byte[] buffer = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                }

                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Monitor.Log($"HTTP handling error: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            CacheProgression();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            CacheProgression();
        }

        private void CacheProgression()
        {
            if (!Context.IsWorldReady) return;
            _cachedMineDepth = Game1.player.deepestMineLevel;
            _hasSkullKey = Game1.MasterPlayer.mailReceived.Contains("HasSkullKey");
            _hasRustyKey = Game1.MasterPlayer.mailReceived.Contains("HasRustyKey");

            var pool = MonsterProgression.Where(m =>
                _cachedMineDepth >= m.MinMineDepth &&
                (!m.NeedsSkullKey || _hasSkullKey)
            ).Select(m => m.Type).ToList();

            Monitor.Log($"[Progression] Шахта: {_cachedMineDepth} | SkullKey: {_hasSkullKey} | RustyKey: {_hasRustyKey} | Доступні монстри: {string.Join(", ", pool)}", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (_freezeTicks > 0)
            {
                Game1.player.forceTimePass = true;
                Game1.player.canMove = false;
                Game1.player.canOnlyWalk = true;
                if (--_freezeTicks == 0)
                {
                    Game1.player.forceTimePass = false;
                    Game1.player.canMove = true;
                    Game1.player.canOnlyWalk = false;
                    _freezeActive = false;
                    Game1.addHUDMessage(new HUDMessage("Розморожено!", HUDMessage.newQuest_type));
                }
            }

            if (_reverseTicks > 0 && --_reverseTicks == 0)
            {
                RestoreMoveBindings();
            }

            while (_commandQueue.TryDequeue(out var cmd))
            {
                ExecuteCommand(cmd);
            }
        }

        private void ExecuteCommand(CommandPayload cmd)
        {
            Monitor.Log($"[Firebot] Виконання дії: {cmd.Action} (ID: {cmd.Id})", LogLevel.Info);
            int amount = cmd.Amount;
            int duration = cmd.Duration > 0 ? cmd.Duration : 10;

            switch (cmd.Action.ToLower())
            {
                case "heal":
                    Game1.player.health = Game1.player.maxHealth;
                    Game1.player.stamina = Game1.player.maxStamina.Value;
                    Game1.addHUDMessage(new HUDMessage("HP та Енергію відновлено!", HUDMessage.newQuest_type));
                    break;

                case "spawn_item":
                    if (string.IsNullOrEmpty(cmd.Id) || cmd.Id.Equals("random", StringComparison.OrdinalIgnoreCase))
                    {
                        var (rndId, min, max) = PickRandomItem();
                        int rndCount = min >= max ? min : _random.Next(min, max + 1);
                        Item? item = ItemRegistry.Create($"(O){rndId}", rndCount);
                        if (item != null)
                        {
                            Game1.player.addItemByMenuIfNecessary(item);
                            Game1.addHUDMessage(new HUDMessage($"Отримано: {item.DisplayName} x{rndCount}", HUDMessage.newQuest_type));
                        }
                    }
                    else if (!string.IsNullOrEmpty(cmd.Id))
                    {
                        int count = amount > 0 ? amount : 1;
                        string itemId = cmd.Id.StartsWith("(") ? cmd.Id : $"(O){cmd.Id}";
                        Item? item = ItemRegistry.Create(itemId, count);
                        if (item != null)
                        {
                            Game1.player.addItemByMenuIfNecessary(item);
                            Game1.addHUDMessage(new HUDMessage($"Отримано: {item.DisplayName} x{count}", HUDMessage.newQuest_type));
                        }
                    }
                    break;

                case "drink":
                case "eat":
                    string drinkId = string.IsNullOrEmpty(cmd.Id) ? "(O)346" : cmd.Id;
                    if (ItemRegistry.Create(drinkId) is StardewValley.Object drinkObj)
                    {
                        Game1.player.eatObject(drinkObj);
                    }
                    break;

                case "cash":
                    int gain = amount != 0 ? amount : 500;
                    Game1.player.Money += gain;
                    Game1.addHUDMessage(new HUDMessage($"+{gain}G", HUDMessage.newQuest_type));
                    break;

                case "crash":
                    int loss = amount != 0 ? amount : 1000;
                    Game1.player.Money = Math.Max(0, Game1.player.Money - loss);
                    Game1.addHUDMessage(new HUDMessage($"-{loss}G", HUDMessage.error_type));
                    break;

                case "health":
                    int hpChange = amount != 0 ? amount : 15;
                    Game1.player.health = Math.Clamp(Game1.player.health + hpChange, 1, Game1.player.maxHealth);
                    break;

                case "energy":
                    int staminaChange = amount != 0 ? amount : 15;
                    Game1.player.stamina = Math.Clamp(Game1.player.stamina + staminaChange, -100, Game1.player.maxStamina.Value);
                    break;

                case "noise":
                case "sound":
                    string sound = string.IsNullOrEmpty(cmd.Id) ? GetRandomSound() : cmd.Id;
                    Game1.playSound(sound);
                    Monitor.Log($"[Sound] Відтворено звук: {sound}", LogLevel.Info);
                    break;

                case "bomb":
                    int bombCount = cmd.Amount > 0 ? cmd.Amount : 1;
                    for (int i = 0; i < bombCount; i++)
                    {
                        SpawnBomb(cmd.Id);
                    }
                    break;

                case "warp":
                    TeleportPlayer(cmd.Target, cmd.X, cmd.Y);
                    break;

                case "hat":
                    string hatId = string.IsNullOrEmpty(cmd.Id) ? _random.Next(0, 90).ToString() : cmd.Id;
                    Game1.player.hat.Value = new Hat(hatId);
                    break;

                case "shuffle":
                    var items = Game1.player.Items.ToList();
                    var shuffled = items.OrderBy(_ => _random.Next()).ToList();
                    for (int i = 0; i < items.Count; i++)
                    {
                        Game1.player.Items[i] = shuffled[i];
                    }
                    Game1.addHUDMessage(new HUDMessage("Інвентар перемішано!", HUDMessage.error_type));
                    break;

                    case "delete":
                        var validIndices = Enumerable.Range(0, Game1.player.Items.Count)
                            .Where(i => Game1.player.Items[i] != null).ToList();
                        if (validIndices.Count > 0)
                        {
                            int indexToRemove = validIndices[_random.Next(validIndices.Count)];
                            Item itemToRemove = Game1.player.Items[indexToRemove];
                            string itemName = itemToRemove.DisplayName;

                            Game1.player.Items[indexToRemove] = null;
                            Game1.addHUDMessage(new HUDMessage($"Видалено предмет: {itemName}", HUDMessage.error_type));

                            // Вивід у консоль SMAPI та лог-файл:
                            Monitor.Log($"[Delete] Видалено предмет з інвентаря: {itemName}", LogLevel.Info);
                        }
                        else
                        {
                            Monitor.Log("[Delete] Інвентар порожній, нічого видаляти.", LogLevel.Warn);
                        }
                        break;

                case "speed":
                    int speedVal = amount != 0 ? amount : 3;
                    Game1.player.applyBuff(new Buff("firebot_speed", "Speed Boost", "Speed", duration * 1000, null, -1, new BuffEffects { Speed = { speedVal } }));
                    break;

                case "slug":
                    int slowVal = amount != 0 ? -amount : -3;
                    Game1.player.applyBuff(new Buff("firebot_slug", "Slowness", "Slug", duration * 1000, null, -1, new BuffEffects { Speed = { slowVal } }));
                    break;

                case "freeze":
                    int freezeSec = duration > 0 ? duration : 5;
                    if (_freezeActive) break;
                    _freezeActive = true;
                    Game1.player.forceTimePass = true;
                    Game1.player.canMove = false;
                    Game1.player.canOnlyWalk = true;
                    _freezeTicks = freezeSec * 60;
                    Game1.addHUDMessage(new HUDMessage($"Заморожено на {freezeSec} сек!", HUDMessage.error_type));
                    break;

                case "reverse":
                    int reverseSec = duration > 0 ? duration : 10;
                    if (_reverseActive) break;
                    if (!_freezeActive && !_reverseActive)
                    {
                        _origMoveUp = Game1.options.moveUpButton[0];
                        _origMoveDown = Game1.options.moveDownButton[0];
                        _origMoveLeft = Game1.options.moveLeftButton[0];
                        _origMoveRight = Game1.options.moveRightButton[0];
                    }
                    _reverseActive = true;
                    Game1.options.moveUpButton[0] = _origMoveDown;
                    Game1.options.moveDownButton[0] = _origMoveUp;
                    Game1.options.moveLeftButton[0] = _origMoveRight;
                    Game1.options.moveRightButton[0] = _origMoveLeft;
                    _reverseTicks = reverseSec * 60;
                    Game1.addHUDMessage(new HUDMessage($"Керування інвертовано на {reverseSec} сек!", HUDMessage.error_type));
                    break;

                case "npc":
                    var allNpcs = Utility.getAllCharacters();
                    var villagers = allNpcs.Where(c => c is not null && c.IsVillager).ToList();
                    if (villagers.Count == 0)
                        villagers = allNpcs.Where(c => c is not null).ToList();
                    if (villagers.Count > 0)
                    {
                        var npc = villagers[_random.Next(villagers.Count)];
                        npc.CurrentDialogue.Push(new Dialogue(npc, "en", "Привіт від глядачів Твіча!"));
                        Game1.drawDialogue(npc);
                    }
                    break;

                case "fish":
                    string fishId = string.IsNullOrEmpty(cmd.Id) ? "(O)142" : cmd.Id;
                    Game1.activeClickableMenu = new BobberBar(fishId, 0.5f, false, new List<string>(), "", false, "", false);
                    break;

                case "monster":
                case "spawn_monster":
                    int monsterCount = cmd.Amount > 0 ? cmd.Amount : 1;
                    for (int i = 0; i < monsterCount; i++)
                    {
                        SpawnMonster(cmd.Id);
                    }
                    break;

                case "glitch":
                case "fake_crash":
                    int glitchDuration = cmd.Duration > 0 ? cmd.Duration : 3;
                    TriggerGlitchEffect(glitchDuration);
                    TriggerNpcScream();
                    break;
            }
        }

        private void SpawnBomb(string bombType)
        {
            // Якщо спавнимо кілька бомб — додаємо випадковий зсув на ±1-2 клітинки
            Vector2 targetTile = Game1.player.Tile + new Vector2(_random.Next(-1, 2), _random.Next(-1, 2));

            string type = string.IsNullOrEmpty(bombType) ? "regular" : bombType.ToLower();
            if (type == "random")
            {
                string[] types = { "cherry", "regular", "mega" };
                type = types[_random.Next(types.Length)];
            }

            string bombId = type switch
            {
                "cherry" or "286" or "(O)286" => "(O)286",
                "mega" or "288" or "(O)288" => "(O)288",
                _ => "(O)287"
            };

            // Створюємо предмет і викликаємо placementAction БЕЗ додавання в Game1.currentLocation.objects
            var bombObj = ItemRegistry.Create<StardewValley.Object>(bombId);
            int x = (int)targetTile.X * 64;
            int y = (int)targetTile.Y * 64;

            // placementAction сам запустить таймер вибуху та анімацію ґнота
            bool placed = bombObj.placementAction(Game1.currentLocation, x, y, Game1.player);

            if (placed)
            {
                Monitor.Log($"[Bomb] Активовано бомбу {bombId} на клітинці ({targetTile.X}, {targetTile.Y})", LogLevel.Info);
            }
            else
            {
                Monitor.Log($"[Bomb] Не вдалося встановити бомбу на ({targetTile.X}, {targetTile.Y})", LogLevel.Warn);
            }
        }

        private void SpawnMonster(string monsterType)
        {
            var loc = Game1.currentLocation;
            Vector2 spawnTile = FindValidSpawnTile(loc, Game1.player.Tile, 3);
            Vector2 pixelPos = spawnTile * 64f;

            string type = string.IsNullOrEmpty(monsterType) || monsterType.Equals("random", StringComparison.OrdinalIgnoreCase) || monsterType.Equals("adaptive", StringComparison.OrdinalIgnoreCase)
                ? GetUnlockedMonsterType()
                : monsterType;

            Monster monster = type.ToLower() switch
            {
                "bat" => new Bat(pixelPos),
                "ghost" => new Ghost(pixelPos),
                "skeleton" => new Skeleton(pixelPos),
                "serpent" => new Serpent(pixelPos),
                "shadow" or "shadowbrute" => new ShadowBrute(pixelPos),
                "crab" or "rockcrab" => new RockCrab(pixelPos),
                "mummy" => new Mummy(pixelPos),
                "fly" => new Fly(pixelPos),
                "dust" or "dustspirit" => new DustSpirit(pixelPos),
                _ => new GreenSlime(pixelPos)
            };

            loc.addCharacter(monster);
            Monitor.Log($"[Monster] Заспавнено монстра: {type} (клітинка: {spawnTile.X}, {spawnTile.Y})", LogLevel.Info);
        }

        private Vector2 FindValidSpawnTile(GameLocation loc, Vector2 center, int radius)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 tile = center + new Vector2(_random.Next(-radius, radius + 1), _random.Next(-radius, radius + 1));
                int tx = (int)tile.X;
                int ty = (int)tile.Y;
                if (loc.isTilePassable(tile) && !loc.isWaterTile(tx, ty) && !loc.IsTileOccupiedBy(tile))
                    return tile;
            }
            Monitor.Log("[Monster] Не знайдено прохідний тайл, спавн на тайлі гравця.", LogLevel.Warn);
            return center;
        }

        private string GetUnlockedMonsterType()
        {
            var pool = MonsterProgression.Where(m =>
                _cachedMineDepth >= m.MinMineDepth &&
                (!m.NeedsSkullKey || _hasSkullKey)
            ).Select(m => m.Type).ToList();

            return pool.Count > 0
                ? pool[_random.Next(pool.Count)]
                : "slime";
        }

        private bool IsLocationUnlocked(GameLocation loc)
        {
            var data = loc?.GetData();
            return data != null && data.DefaultArrivalTile != null;
        }

        private void TeleportPlayer(string targetLocation, int x, int y)
        {
            GameLocation locationToWarp;
            if (string.Equals(targetLocation, "random", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(targetLocation))
            {
                var candidates = SafeWarpLocations
                    .Select(Game1.getLocationFromName)
                    .Where(IsLocationUnlocked)
                    .ToList();
                if (candidates.Count == 0)
                {
                    Monitor.Log("[Warp] Немає безпечних локацій для телепорту.", LogLevel.Warn);
                    return;
                }
                locationToWarp = candidates[_random.Next(candidates.Count)];
            }
            else
            {
                locationToWarp = Game1.getLocationFromName(targetLocation);
                if (locationToWarp == null)
                {
                    Monitor.Log($"[Warp] Локацію '{targetLocation}' не знайдено.", LogLevel.Warn);
                    return;
                }
            }

            Vector2 tile;
            if (x >= 0 && y >= 0)
            {
                tile = new Vector2(x, y);
            }
            else if (locationToWarp.GetData()?.DefaultArrivalTile is Point arrivalTile)
            {
                tile = new Vector2(arrivalTile.X, arrivalTile.Y);
            }
            else
            {
                Monitor.Log($"[Warp] У локації '{locationToWarp.Name}' немає точки прибуття, пропускаю.", LogLevel.Warn);
                return;
            }

            Game1.warpFarmer(locationToWarp.Name, (int)tile.X, (int)tile.Y, false);
        }

        private void RestoreMoveBindings()
        {
            if (!_reverseActive) return;
            Game1.options.moveUpButton[0] = _origMoveUp;
            Game1.options.moveDownButton[0] = _origMoveDown;
            Game1.options.moveLeftButton[0] = _origMoveLeft;
            Game1.options.moveRightButton[0] = _origMoveRight;
            _reverseActive = false;
            Game1.addHUDMessage(new HUDMessage("Керування відновлено.", HUDMessage.newQuest_type));
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (_freezeTicks > 0)
            {
                int secs = (_freezeTicks + 59) / 60;
                string text = $"Заморожено: {secs}с";
                var font = Game1.dialogueFont;
                var pos = new Vector2(16, 16);
                e.SpriteBatch.DrawString(font, text, pos + new Vector2(2, 2), Color.Black * 0.6f);
                e.SpriteBatch.DrawString(font, text, pos, Color.LightCyan);
            }

            if (_glitchTicks > 0)
            {
                _glitchTicks--;

                var batch = e.SpriteBatch;
                var pixel = GetPixelTexture(batch);
                var vp = Game1.uiViewport;
                int vw = vp.Width;
                int vh = vp.Height;

                if (_random.NextDouble() < 0.3)
                {
                    batch.Draw(pixel, new Rectangle(0, 0, vw, vh), Color.Red * 0.2f);
                }
                else if (_random.NextDouble() < 0.3)
                {
                    batch.Draw(pixel, new Rectangle(0, 0, vw, vh), Color.Cyan * 0.2f);
                }

                int numStripes = _random.Next(10, 25);
                for (int i = 0; i < numStripes; i++)
                {
                    int y = _random.Next(0, vh);
                    int height = _random.Next(2, 20);
                    Color color = _random.Next(2) == 0 ? Color.Black : Color.White;
                    batch.Draw(pixel, new Rectangle(0, y, vw, height), color * (float)_random.NextDouble());
                }
            }
        }
        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (Game1.activeClickableMenu is GameMenu menu && menu.currentTab == GameMenu.inventoryTab)
            {
                DrawInfoPanel(e.SpriteBatch);
                DrawMysteryBoxPanel(e.SpriteBatch);
            }
            else if (Game1.activeClickableMenu is GeodeMenu)
            {
                DrawInfoPanel(e.SpriteBatch);
                DrawMysteryBoxPanel(e.SpriteBatch);
            }
        }

        private void DrawInfoPanel(SpriteBatch b)
        {
            EnsureGeodeList();
            RefreshGeodeCache();

            var activeMenu = Game1.activeClickableMenu;
            if (activeMenu == null) return;

            var font = Game1.smallFont;
            float lineH = font.MeasureString("Ag").Y + 6;

            double luck = Game1.player.DailyLuck;
            int buffLuck = Game1.player.buffs.LuckLevel;
            string weatherText = $"Завтра: {GetWeatherLabel(Game1.weatherForTomorrow)}";
            string luckText = $"Удача: {luck.ToString("+0.###;-0.###;0")} ({GetLuckLabel(luck)})";
            string buffText = $"Баф удачі: {buffLuck.ToString("+0;-0;0")}";
            Color luckColor = luck >= 0.02 ? Color.LightGreen : (luck <= -0.02 ? Color.Salmon : Color.White);
            Color buffColor = buffLuck > 0 ? Color.LightGreen : Color.White;

            bool hasGeodes = _geodeItems.Count > 0 && _geodeLootCache.Count > 0;
            uint counter = Game1.stats.GeodesCracked;
            string step1 = $"Наступна (#{counter + 1}):";
            string step2 = $"Через одну (#{counter + 2}):";
            string header = "Прогноз жеод";

            float iconW = 40f;
            float maxW = font.MeasureString(weatherText).X;
            maxW = Math.Max(maxW, font.MeasureString(luckText).X);
            maxW = Math.Max(maxW, font.MeasureString(buffText).X);
            if (hasGeodes)
            {
                maxW = Math.Max(maxW, font.MeasureString(header).X);
                maxW = Math.Max(maxW, font.MeasureString(step1).X);
                for (int i = 0; i < _geodeItems.Count; i++)
                {
                    string label = GetGeodeLabel(_geodeItems[i]);
                    foreach (var (lootText, _) in _geodeLootCache[_geodeItems[i].QualifiedItemId])
                    {
                        float w = iconW + font.MeasureString($"{label}: {lootText}").X;
                        maxW = Math.Max(maxW, w);
                    }
                }
            }

            float panelW = maxW + 24;
            float x = activeMenu.xPositionOnScreen - panelW - 8;
            if (x < 4) x = 4;
            float textX = x + 12;
            float geodeTextX = textX + iconW;

            float blockH;
            if (hasGeodes)
            {
                blockH = (3 + 1 + 2 * (1 + _geodeItems.Count)) * lineH + 26;
            }
            else
            {
                blockH = 3 * lineH;
            }
            float startY = Math.Max(8, (Game1.uiViewport.Height - blockH) / 2f);

            var pixel = GetPixelTexture(b);
            b.Draw(pixel, new Rectangle((int)x - 4, (int)startY - 10, (int)panelW, (int)blockH + 24), Color.Black * 0.55f);

            float y = startY;
            Utility.drawTextWithShadow(b, weatherText, font, new Vector2(textX, y), Color.White);
            y += lineH;
            Utility.drawTextWithShadow(b, luckText, font, new Vector2(textX, y), luckColor);
            y += lineH;
            Utility.drawTextWithShadow(b, buffText, font, new Vector2(textX, y), buffColor);
            y += lineH;

            if (!hasGeodes) return;

            y += 8;
            b.Draw(pixel, new Rectangle((int)textX, (int)y, (int)maxW, 2), Color.White * 0.25f);
            y += 12;

            Utility.drawTextWithShadow(b, header, font, new Vector2(textX, y), Color.Gold);
            y += lineH + 2;

            for (int step = 0; step < 2; step++)
            {
                string stepHeader = step == 0 ? step1 : step2;
                Utility.drawTextWithShadow(b, stepHeader, font, new Vector2(textX, y), Color.White);
                y += lineH;

                for (int i = 0; i < _geodeItems.Count; i++)
                {
                    var geode = _geodeItems[i];
                    string geodeId = geode.QualifiedItemId;
                    string label = GetGeodeLabel(geode);
                    var (lootText, rare) = _geodeLootCache[geodeId][step];

                    geode.drawInMenu(b, new Vector2(textX, y - 2), 0.5f);

                    Color lootColor = rare ? Color.Gold : Color.White;
                    Utility.drawTextWithShadow(b, $"{label}: {lootText}", font, new Vector2(geodeTextX, y), lootColor);
                    y += lineH;
                }
                y += 8;
            }
        }

        private static string GetGeodeLabel(Item geode) => geode.QualifiedItemId switch
        {
            "(O)535" => "Зеода",
            "(O)536" => "Замерзла",
            "(O)537" => "Магмова",
            "(O)749" => "Omni",
            "(O)116" => "Скриня",
            "(O)791" => "Кокос",
            _ => geode.DisplayName
        };

        private void EnsureGeodeList()
        {
            if (_geodeListInitialized) return;
            _geodeListInitialized = true;
            _geodeItems.Clear();
            for (int i = 0; i < GeodeIds.Length; i++)
            {
                try
                {
                    Item? geode = ItemRegistry.Create(GeodeIds[i]);
                    if (geode != null && Utility.IsGeode(geode))
                    {
                        _geodeItems.Add(geode);
                    }
                }
                catch
                {
                }
            }
        }

        private void RefreshGeodeCache()
        {
            uint counter = Game1.stats.GeodesCracked;
            if (counter == _geodeCacheCounter && _geodeLootCache.Count > 0) return;

            _geodeCacheCounter = counter;
            _geodeLootCache.Clear();

            uint orig = counter;
            try
            {
                foreach (var geode in _geodeItems)
                {
                    var entries = new (string LootText, bool Rare)[2];
                    for (int step = 1; step <= 2; step++)
                    {
                        Game1.stats.GeodesCracked = orig + (uint)step;
                        Item? loot = Utility.getTreasureFromGeode(geode);
                        if (loot != null)
                        {
                            string text = loot.Stack > 1 ? $"{loot.DisplayName} x{loot.Stack}" : loot.DisplayName;
                            bool rare = RareLootIds.Contains(loot.QualifiedItemId);
                            entries[step - 1] = (text, rare);
                        }
                        else
                        {
                            entries[step - 1] = ("?", false);
                        }
                    }
                    _geodeLootCache[geode.QualifiedItemId] = entries;
                }
            }
            finally
            {
                Game1.stats.GeodesCracked = orig;
            }
        }

        private void DrawMysteryBoxPanel(SpriteBatch b)
        {
            EnsureBoxList();
            RefreshMysteryBoxCache();

            if (_boxItems.Count == 0 || _boxLootCache.Count == 0) return;

            var activeMenu = Game1.activeClickableMenu;
            if (activeMenu == null) return;

            var font = Game1.smallFont;
            float lineH = font.MeasureString("Ag").Y + 6;

            string header = "Прогноз скриньок";
            uint boxCounter = Game1.stats.Get("MysteryBoxesOpened");
            string step1 = $"Наступна (#{boxCounter + 1}):";
            string step2 = $"Через одну (#{boxCounter + 2}):";

            float iconW = 40f;
            float maxW = font.MeasureString(header).X;
            maxW = Math.Max(maxW, font.MeasureString(step1).X);
            maxW = Math.Max(maxW, font.MeasureString(step2).X);
            for (int i = 0; i < _boxItems.Count; i++)
            {
                string label = GetBoxLabel(_boxItems[i]);
                foreach (var (lootText, _) in _boxLootCache[_boxItems[i].QualifiedItemId])
                {
                    float w = iconW + font.MeasureString($"{label}: {lootText}").X;
                    maxW = Math.Max(maxW, w);
                }
            }

            float panelW = maxW + 24;
            float blockH = (3 + 2 * _boxItems.Count) * lineH + 18;
            float startY = Math.Max(8, (Game1.uiViewport.Height - blockH) / 2f);

            float viewportW = Game1.uiViewport.Width;
            float x = activeMenu.xPositionOnScreen + activeMenu.width + 8;
            if (x + panelW > viewportW - 4)
            {
                x = Math.Max(4, viewportW - panelW - 4);
            }
            float textX = x + 12;
            float iconTextX = textX + iconW;

            var pixel = GetPixelTexture(b);
            b.Draw(pixel, new Rectangle((int)x - 4, (int)startY - 10, (int)panelW, (int)blockH + 24), Color.Black * 0.55f);

            float y = startY;
            Utility.drawTextWithShadow(b, header, font, new Vector2(textX, y), Color.Gold);
            y += lineH + 2;

            for (int step = 0; step < 2; step++)
            {
                string stepHeader = step == 0 ? step1 : step2;
                Utility.drawTextWithShadow(b, stepHeader, font, new Vector2(textX, y), Color.White);
                y += lineH;

                for (int i = 0; i < _boxItems.Count; i++)
                {
                    var box = _boxItems[i];
                    string boxId = box.QualifiedItemId;
                    string label = GetBoxLabel(box);
                    var (lootText, rare) = _boxLootCache[boxId][step];

                    box.drawInMenu(b, new Vector2(textX, y - 2), 0.5f);

                    Color lootColor = rare ? Color.Gold : Color.White;
                    Utility.drawTextWithShadow(b, $"{label}: {lootText}", font, new Vector2(iconTextX, y), lootColor);
                    y += lineH;
                }
                y += 8;
            }
        }

        private static string GetBoxLabel(Item box) => box.QualifiedItemId switch
        {
            "(O)MysteryBox" => "Таємнича",
            "(O)GoldenMysteryBox" => "Золота",
            _ => box.DisplayName
        };

        private void EnsureBoxList()
        {
            if (_boxListInitialized) return;
            _boxListInitialized = true;
            _boxItems.Clear();
            for (int i = 0; i < BoxIds.Length; i++)
            {
                try
                {
                    Item? box = ItemRegistry.Create(BoxIds[i]);
                    if (box != null && Utility.IsGeode(box))
                    {
                        _boxItems.Add(box);
                    }
                }
                catch
                {
                }
            }
        }

        private void RefreshMysteryBoxCache()
        {
            uint counter = Game1.stats.Get("MysteryBoxesOpened");
            if (counter == _boxCacheCounter && _boxLootCache.Count > 0) return;

            _boxCacheCounter = counter;
            _boxLootCache.Clear();

            uint orig = counter;
            bool hadBook = Game1.player.mailReceived.Contains("GotMysteryBook");
            try
            {
                foreach (var box in _boxItems)
                {
                    var entries = new (string LootText, bool Rare)[2];
                    for (int step = 1; step <= 2; step++)
                    {
                        Game1.stats.Set("MysteryBoxesOpened", orig + (uint)step);
                        if (!hadBook) Game1.player.mailReceived.Remove("GotMysteryBook");

                        Item? loot = Utility.getTreasureFromGeode(box);
                        if (loot != null)
                        {
                            string text = loot.Stack > 1 ? $"{loot.DisplayName} x{loot.Stack}" : loot.DisplayName;
                            bool rare = RareLootIds.Contains(loot.QualifiedItemId);
                            entries[step - 1] = (text, rare);
                        }
                        else
                        {
                            entries[step - 1] = ("?", false);
                        }

                        if (!hadBook) Game1.player.mailReceived.Remove("GotMysteryBook");
                        Game1.stats.Set("MysteryBoxesOpened", orig);
                    }
                    _boxLootCache[box.QualifiedItemId] = entries;
                }
            }
            finally
            {
                if (!hadBook) Game1.player.mailReceived.Remove("GotMysteryBook");
                Game1.stats.Set("MysteryBoxesOpened", orig);
            }
        }

        private static string GetWeatherLabel(string weather) => weather switch
        {
            "Sun" => "Сонячно",
            "Rain" => "Дощ",
            "Storm" => "Гроза",
            "Wind" => "Вітер",
            "Snow" => "Сніг",
            "GreenRain" => "Зелений дощ",
            "Wedding" => "Весілля",
            "Festival" => "Фестиваль",
            _ => weather
        };

        private static string GetLuckLabel(double luck) => luck switch
        {
            >= 0.07 => "Чудова",
            >= 0.02 => "Добра",
            > -0.02 => "Нормальна",
            > -0.07 => "Погана",
            _ => "Жахлива"
        };

        private Texture2D GetPixelTexture(SpriteBatch b)
        {
            if (_pixelTexture == null)
            {
                _pixelTexture = new Texture2D(b.GraphicsDevice, 1, 1);
                _pixelTexture.SetData(new[] { Color.White });
            }
            return _pixelTexture;
        }

        private void TriggerGlitchEffect(int durationSec)
        {
            _glitchTicks = durationSec * 60;
            Game1.playSound("thunder");
        }

        private void TriggerNpcScream()
        {
            var villagers = Utility.getAllCharacters().Where(c => c is not null && c.IsVillager).ToList();
            if (villagers.Count == 0) return;

            var npc = villagers[_random.Next(villagers.Count)];
            string glitchText = GenerateGlitchString(_random.Next(30, 70));

            string sound = CreepySounds[_random.Next(CreepySounds.Length)];
            Game1.playSound(sound);

            if (Game1.activeClickableMenu == null)
            {
                npc.CurrentDialogue.Push(new Dialogue(npc, "en", glitchText));
                Game1.drawDialogue(npc);
            }
        }

        private string GenerateGlitchString(int length)
        {
            const string chars = "#@%&*!?01<>{}[]/\\|=+-_^~";
            StringBuilder result = new();
            for (int i = 0; i < length; i++)
            {
                result.Append(chars[_random.Next(chars.Length)]);
            }
            return result.ToString();
        }

        private string GetRandomSound()
        {
            string[] sounds = {
                // Тварини та істоти
                "Duck", "cluck", "rooster", "cat", "cow", "pig", "owl", "crow",
                "ghost", "parrot", "slime", "goat", "dinosaur", "mummyGasp",

                // Магія, бонуси та UI
                "thunder", "gasp", "stardrop", "achievement", "explosion", "fireball",
                "wand", "stairsdown", "secret1", "reward", "yoba", "portal", "healSound",
                "levelUp", "questcomplete", "newArtifact", "crystal", "fairy", "powerup",

                // Дії, зброя та предмети
                "hammer", "coin", "swordswipe", "dirtyHit", "discoverMineral",
                "axe", "hoe", "scissors", "trashcan", "openChest", "shipment",
                "boulderBreak", "clubhit", "crit", "slash", "cast", "pullItemFromWater",

                // Кумедні, побутові та міні-ігри
                "dwop", "boop", "pluck", "glug", "sip", "eating", "gulp", "grunt",
                "clack", "purchase", "sell", "bubbles", "furnace", "woodWhack"
            };
            return sounds[_random.Next(sounds.Length)];
        }
    }
}
