using Silk.NET.SDL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.Maths;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.IO;
using StbImageSharp;

// -- SETUP --
var sdl = Sdl.GetApi();
var options = WindowOptions.Default;
options.Size = new Vector2D<int>(1280, 720);
options.Title = "Bullet Heaven: Origins";
options.WindowState = WindowState.Fullscreen;

using var window = Silk.NET.Windowing.Window.Create(options);

unsafe
{
    // --- TEXTURE VARIABLES ---
    Texture* texPlayer = null;
    Texture* texEnemy = null;
    Texture* texBullet = null;
    Texture* texGem = null;
    Texture* texBoss = null;

    // --- GUI ASSETS ---
    Texture* texUiSheet = null; // Load 'assets/ui_sheet.png' here!
    Texture* texPanel = null;   // Load 'assets/ui_panel.png' here!
    
    // --- TILE MAP SYSTEM ---
    Texture* texTileset = null; // Load this new image here!
    int tileSize = 64;          // Made this bigger!
    int mapColumns = 20;        // 1280 / 64 = 20
    int mapRows = 12;           // 720 / 64 = 11.25 (rounded up to 12)
    
    int[,] tileMap = new int[mapColumns, mapRows];

    // Fill the whole map with just this grass (Tile 0)
    for (int x = 0; x < mapColumns; x++)
    {
        for (int y = 0; y < mapRows; y++)
        {
            tileMap[x, y] = 0; 
        }
    }

    // --- GAME STATE VARIABLES ---
    GameState currentState = GameState.MainMenu;
    GameState previousState = GameState.Playing;

    // --- STRUCTURE VARIABLES ---
    List<Structure> obstacles = new List<Structure>();
    Texture* texStructure = null; // Load 'assets/structure.png' in window.Load

    // --- SHOP ASSETS ---
    Texture* texShopBg = null;      // Load 'assets/shop_bg.png'
    Texture* texWeaponPad = null;   // Load 'assets/weapon_pad.png'
    Texture* texShieldPad = null;   // Load 'assets/shield_pad.png'
    Texture* texExitDoor = null;    // Load 'assets/exit_door.png'

    // --- MENU ASSETS ---
    Texture* texMenuBg = null;   // Your background art
    Texture* texPlayBtn = null;  // Your custom button sprite
    

    // Let's manually place a few "Rocks" or "Walls"
    
    obstacles.Add(new Structure { Hitbox = new Rectangle<int>(new Vector2D<int>(400, 300), new Vector2D<int>(64, 64)) });
    obstacles.Add(new Structure { Hitbox = new Rectangle<int>(new Vector2D<int>(800, 200), new Vector2D<int>(128, 64)) });
    obstacles.Add(new Structure { Hitbox = new Rectangle<int>(new Vector2D<int>(200, 150), new Vector2D<int>(64, 256)) });
    
    // 2. A long horizontal wall on the bottom right
    obstacles.Add(new Structure { Hitbox = new Rectangle<int>(new Vector2D<int>(750, 550), new Vector2D<int>(256, 64)) });
    
    // 3. A large square block in the top right corner
    obstacles.Add(new Structure { Hitbox = new Rectangle<int>(new Vector2D<int>(1000, 100), new Vector2D<int>(128, 128)) });
    
    int currentLevel = 1;
    int gemsCurrency = 0; 
    int enemiesToSpawnThisLevel = 10; 

    int shieldHealth = 0;
    int maxShield = 50;

    

    bool isWaveEnding = false;
    float waveEndTimer = 0f;

    IInputContext? inputContext = null;   // Added '?' to fix CS8600 warning
    IKeyboard? primaryKeyboard = null;    // Added '?' to fix CS8600 warning
    IMouse? primaryMouse = null;          // Added '?' to fix CS8600 warning
    
    Random rand = new Random();           // <--- THIS FIXES THE CRASH! 
    Renderer* renderer = null;
    
    Vector2 playerPos = new Vector2(640, 360);
    float playerSpeed = 400f;
    // --- PLAYER ANIMATION ---
    int playerFrameWidth = 32;  
    int playerFrameHeight = 32; 
    int playerTotalFrames = 6;  
    float playerScale = 2.0f;   // Makes the character nice and visible

    int playerCurrentFrame = 0;
    float playerAnimTimer = 0f;
    float playerAnimSpeed = 0.1f; 
    RendererFlip playerFlip = RendererFlip.None; // Controls Left/Right mirroring
    
    List<Projectile> bullets = new List<Projectile>(); 
    float fireTimer = 0f;
    float fireRate = 0.2f; 
    float bulletSpeed = 600f;

    // --- ENEMY & ANIMATION VARIABLES ---
    List<Enemy> enemies = new List<Enemy>(); // <-- Changed from Vector2 to Enemy!
    float enemySpawnTimer = 0f;
    float enemySpawnRate = 1.0f; 
    float enemySpeed = 150f; 
    
    int enemyFrameWidth = 64;   // Frame Width
    int enemyFrameHeight = 64;  // Frame Height
    int enemyWalkFrames = 4;    // Loop first 4 frames
    float enemyAnimSpeed = 0.15f;
    float enemyScale = 2.0f;

    // --- BOSS VARIABLES ---
    bool isBossActive = false;
    Vector2 bossPos = Vector2.Zero;
    int bossHealth = 100;
    
    // NEW: Boss Animation trackers
    int bossFrameWidth = 64;  
    int bossFrameHeight = 64; 
    int bossCurrentFrame = 0;
    int bossCurrentRow = 0;   
    float bossAnimTimer = 0f;

    List<Vector2> gems = new List<Vector2>();
    float pickupRadius = 60f; 
    int gemFrameWidth = 32;  // <--- Changed to 32!
    int gemFrameHeight = 32; // <--- Changed to 32!
    float gemScale = 1.0f;
    
    int playerMaxHealth = 100;
    int playerHealth = 100;
    bool isGameOver = false;
    bool escapeKeyPressedLastFrame = false;

    window.Load += () =>
    {
        var nativeWindow = (Silk.NET.SDL.Window*)window.Native!.Sdl!.Value;
        renderer = sdl.CreateRenderer(nativeWindow, -1, (uint)RendererFlags.Accelerated);
        if (renderer == null) Console.WriteLine("Renderer failed to load!");

        sdl.RenderSetLogicalSize(renderer, 1280, 720);

         

        // Force Crisp Pixel Art
        sdl.SetHint(Sdl.HintRenderScaleQuality, "0");
        sdl.SetRenderDrawBlendMode(renderer, BlendMode.Blend); 

        inputContext = window.CreateInput();
        if (inputContext.Keyboards.Count > 0) primaryKeyboard = inputContext.Keyboards[0];
        if (inputContext.Mice.Count > 0) primaryMouse = inputContext.Mice[0]; 

        Texture* LoadTexture(string path)
        {
            if (!File.Exists(path)) {
                Console.WriteLine($"Could not find image: {path}");
                return null;
            }
            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            Texture* tex = null;
            fixed (byte* ptr = image.Data) {
                uint rMask = 0x000000FF;
                uint gMask = 0x0000FF00;
                uint bMask = 0x00FF0000;
                uint aMask = 0xFF000000;
                var surface = sdl.CreateRGBSurfaceFrom(ptr, image.Width, image.Height, 32, image.Width * 4, rMask, gMask, bMask, aMask);
                tex = sdl.CreateTextureFromSurface(renderer, surface);
                sdl.FreeSurface(surface);
            }
            return tex;
        }

        // texPlayer = LoadTexture("assets/player.png");
        // texEnemy = LoadTexture("assets/enemy.png");
        // texBullet = LoadTexture("assets/bullet.png");
        // texGem = LoadTexture("assets/gem.png");
        // texTileset = LoadTexture("assets/tileset.png"); 
        // texStructure = LoadTexture("assets/structure.png");
        // texBoss = LoadTexture("assets/boss.png");

        //shop assets
        // texShopBg = LoadTexture("assets/shop_bg.png");
        // texWeaponPad = LoadTexture("assets/weapon_pad.png");
        // texShieldPad = LoadTexture("assets/shield_pad.png");
        // texExitDoor = LoadTexture("assets/exit_door.png");

        
        // texPanel = LoadTexture("assets/ui_panel.png");
        // texUiSheet = LoadTexture("assets/ui_sheet.png");

        // texMenuBg = LoadTexture("assets/menu_bg.png");
        // texPlayBtn = LoadTexture("assets/play_button.png");
    };

    // --- FULL RETRO ALPHABET FONT RENDERER ---
    void DrawText(string text, int x, int y, int size)
    {
        text = text.ToUpper();
        int cursorX = x;
        foreach (char c in text)
        {
            if (c == ' ') { cursorX += 4 * size; continue; }
            
            // 3x5 Grid compressed into numbers: 7=Full Row, 5=Edges, 2=Middle, etc.
            int[] g = new int[5]; 
            if (c=='A') g = new[]{7,5,7,5,5}; else if (c=='B') g = new[]{6,5,6,5,6}; else if (c=='C') g = new[]{7,4,4,4,7};
            else if (c=='D') g = new[]{6,5,5,5,6}; else if (c=='E') g = new[]{7,4,6,4,7}; else if (c=='F') g = new[]{7,4,6,4,4};
            else if (c=='G') g = new[]{7,4,5,5,7}; else if (c=='H') g = new[]{5,5,7,5,5}; else if (c=='I') g = new[]{7,2,2,2,7};
            else if (c=='J') g = new[]{1,1,1,5,7}; else if (c=='K') g = new[]{5,6,4,6,5}; else if (c=='L') g = new[]{4,4,4,4,7};
            else if (c=='M') g = new[]{5,7,5,5,5}; else if (c=='N') g = new[]{6,5,5,5,5}; else if (c=='O'||c=='0') g = new[]{7,5,5,5,7};
            else if (c=='P') g = new[]{7,5,7,4,4}; else if (c=='Q') g = new[]{7,5,5,7,1}; else if (c=='R') g = new[]{7,5,6,5,5};
            else if (c=='S') g = new[]{7,4,7,1,7}; else if (c=='T') g = new[]{7,2,2,2,2}; else if (c=='U') g = new[]{5,5,5,5,7};
            else if (c=='V') g = new[]{5,5,5,5,2}; else if (c=='W') g = new[]{5,5,5,7,5}; else if (c=='X') g = new[]{5,5,2,5,5};
            else if (c=='Y') g = new[]{5,5,2,2,2}; else if (c=='Z') g = new[]{7,1,2,4,7}; else if (c=='1') g = new[]{2,6,2,2,7};
            else if (c=='2') g = new[]{7,1,7,4,7}; else if (c=='3') g = new[]{7,1,7,1,7}; else if (c=='4') g = new[]{5,5,7,1,1};
            else if (c=='5') g = new[]{7,4,7,1,7}; else if (c=='6') g = new[]{7,4,7,5,7}; else if (c=='7') g = new[]{7,1,1,1,1};
            else if (c=='8') g = new[]{7,5,7,5,7}; else if (c=='9') g = new[]{7,5,7,1,7}; else if (c=='-') g = new[]{0,0,7,0,0};
            else if (c=='[') g = new[]{3,2,2,2,3}; else if (c==']') g = new[]{6,2,2,2,6}; else if (c=='|') g = new[]{2,2,2,2,2};
            
            for (int gy = 0; gy < 5; gy++) {
                int row = g[gy];
                // Decode the numbers back into visual pixels
                if ((row & 4) != 0) { var r = new Rectangle<int>(new Vector2D<int>(cursorX, y + (gy * size)), new Vector2D<int>(size, size)); sdl.RenderFillRect(renderer, ref r); }
                if ((row & 2) != 0) { var r = new Rectangle<int>(new Vector2D<int>(cursorX + (1 * size), y + (gy * size)), new Vector2D<int>(size, size)); sdl.RenderFillRect(renderer, ref r); }
                if ((row & 1) != 0) { var r = new Rectangle<int>(new Vector2D<int>(cursorX + (2 * size), y + (gy * size)), new Vector2D<int>(size, size)); sdl.RenderFillRect(renderer, ref r); }
            }
            cursorX += 4 * size; // Move cursor right for the next letter
        }
    }

    bool CheckCollision(Rectangle<int> a, Rectangle<int> b)
    {
        return a.Origin.X < b.Origin.X + b.Size.X &&
               a.Origin.X + a.Size.X > b.Origin.X &&
               a.Origin.Y < b.Origin.Y + b.Size.Y &&
               a.Origin.Y + a.Size.Y > b.Origin.Y;
    }

    window.Update += (delta) =>
    {

        if (primaryKeyboard == null) return;

        // PAUSE LOGIC
        bool escapeCurrentlyPressed = primaryKeyboard.IsKeyPressed(Key.Escape);
        if (escapeCurrentlyPressed && !escapeKeyPressedLastFrame)
        {
            if (currentState == GameState.Playing || currentState == GameState.Shop)
            {
                previousState = currentState;
                currentState = GameState.Paused;
            }
            else if (currentState == GameState.Paused)
            {
                currentState = previousState; 
            }
        }
        escapeKeyPressedLastFrame = escapeCurrentlyPressed;

        if (currentState == GameState.MainMenu)
        {
            window.Title = "MAIN MENU - Press SPACE to Play!";
            if (primaryKeyboard.IsKeyPressed(Key.Space)) currentState = GameState.Playing;
            return; 
        }

        if (currentState == GameState.Paused)
        {
            if (primaryKeyboard.IsKeyPressed(Key.Q)) window.Close(); // Quit to Desktop
            
            if (primaryKeyboard.IsKeyPressed(Key.R))
            {
                isGameOver = false; currentState = GameState.Playing; playerHealth = playerMaxHealth; playerPos = new Vector2(640, 360);
                enemies.Clear(); bullets.Clear(); gems.Clear(); currentLevel = 1; gemsCurrency = 0; enemiesToSpawnThisLevel = 10;
                fireRate = 0.2f; enemySpawnRate = 1.0f; shieldHealth = 0; isBossActive = false; isWaveEnding = false;
            }
            
            // --- NEW: RETURN TO MAIN MENU ---
            if (primaryKeyboard.IsKeyPressed(Key.M))
            {
                currentState = GameState.MainMenu; 
                isGameOver = false; playerHealth = playerMaxHealth; playerPos = new Vector2(640, 360);
                enemies.Clear(); bullets.Clear(); gems.Clear(); currentLevel = 1; gemsCurrency = 0; enemiesToSpawnThisLevel = 10;
                fireRate = 0.2f; enemySpawnRate = 1.0f; shieldHealth = 0; isBossActive = false; isWaveEnding = false;
            }
            return; 
        }

        if (isGameOver)
        {
            window.Title = "GAME OVER - Press R to Restart";
            if (primaryKeyboard.IsKeyPressed(Key.R))
            {
                isGameOver = false; currentState = GameState.Playing; playerHealth = playerMaxHealth; playerPos = new Vector2(640, 360);
                enemies.Clear(); bullets.Clear(); gems.Clear(); currentLevel = 1; gemsCurrency = 0; enemiesToSpawnThisLevel = 10;
                fireRate = 0.2f; enemySpawnRate = 1.0f; shieldHealth = 0; isBossActive = false; isWaveEnding = false;
            }
            return; 
        }

        if (currentState == GameState.Shop)
{
    // HUD Update
    window.Title = $"SHOP: {gemsCurrency} Gems. Left = Weapon (5), Right = Shield (10). Walk UP to leave.";
    
    Vector2 shopMoveDir = Vector2.Zero;

    // 1. Store the position BEFORE movement
    Vector2 oldPos = playerPos;

    // 2. Input & Flipping
    if (primaryKeyboard.IsKeyPressed(Key.W)) shopMoveDir.Y -= 1;
    if (primaryKeyboard.IsKeyPressed(Key.S)) shopMoveDir.Y += 1;
    
    if (primaryKeyboard.IsKeyPressed(Key.A)) 
    { 
        shopMoveDir.X -= 1; 
        playerFlip = RendererFlip.Horizontal; 
    }
    if (primaryKeyboard.IsKeyPressed(Key.D)) 
    { 
        shopMoveDir.X += 1; 
        playerFlip = RendererFlip.None; 
    }

    // 3. Apply Movement & Animation
    if (shopMoveDir != Vector2.Zero)
    {
        playerPos += Vector2.Normalize(shopMoveDir) * playerSpeed * (float)delta;

        playerAnimTimer += (float)delta;
        if (playerAnimTimer >= playerAnimSpeed)
        {
            playerAnimTimer = 0f;
            playerCurrentFrame = (playerCurrentFrame + 1) % playerTotalFrames;
        }
    }
    else
    {
        playerCurrentFrame = 0; 
    }

    // --- NEW: STRUCTURE COLLISION CHECK ---
    // Define the player's hitbox (assuming 32x32 based on your previous assets)
    var playerRect = new Rectangle<int>(
        new Vector2D<int>((int)playerPos.X - 16, (int)playerPos.Y - 16), 
        new Vector2D<int>(32, 32));

    foreach (var s in obstacles)
    {
        if (CheckCollision(playerRect, s.Hitbox))
        {
            playerPos = oldPos; // Hit an obstacle, revert movement!
            break;
        }
    }

    // 4. Boundary Clamping
    playerPos.X = Math.Clamp(playerPos.X, 20, 1260);
    playerPos.Y = Math.Clamp(playerPos.Y, 20, 700);

    // 5. Shop Item Detection
    if (playerPos.X < 400 && playerPos.Y < 360 && gemsCurrency >= 5) 
    { 
        gemsCurrency -= 5; 
        fireRate = Math.Max(0.05f, fireRate - 0.05f); 
        playerPos.Y = 400; 
    }
    
    if (playerPos.X > 880 && playerPos.Y < 360 && gemsCurrency >= 10 && shieldHealth < maxShield) 
    { 
        gemsCurrency -= 10; 
        shieldHealth = maxShield; 
        playerPos.Y = 400; 
    }
    
    if (playerPos.Y < 50) 
    { 
        currentLevel++; 
        currentState = GameState.Playing; 
        playerPos = new Vector2(640, 360); 
        enemiesToSpawnThisLevel = currentLevel * 15; 
    }
    
    return; 
}

        if (currentState == GameState.Playing)
        {
            // 1. Player Movement & Animation
            Vector2 oldPos = playerPos; 
            Vector2 moveDir = Vector2.Zero;
            if (primaryKeyboard.IsKeyPressed(Key.W)) moveDir.Y -= 1;
            if (primaryKeyboard.IsKeyPressed(Key.S)) moveDir.Y += 1;
            
            if (primaryKeyboard.IsKeyPressed(Key.A)) 
            { 
                moveDir.X -= 1; 
                playerFlip = RendererFlip.Horizontal;
            }
            if (primaryKeyboard.IsKeyPressed(Key.D)) 
            { 
                moveDir.X += 1; 
                playerFlip = RendererFlip.None;
            }

            if (moveDir != Vector2.Zero)
            {
                playerPos += Vector2.Normalize(moveDir) * playerSpeed * (float)delta;

                // Player vs Obstacle Collision
                var pRect = new Rectangle<int>(new Vector2D<int>((int)playerPos.X - 16, (int)playerPos.Y - 16), new Vector2D<int>(32, 32));
                foreach (var s in obstacles)
                {
                    if (CheckCollision(pRect, s.Hitbox)) { playerPos = oldPos; break; }
                }

                playerAnimTimer += (float)delta;
                if (playerAnimTimer >= playerAnimSpeed)
                {
                    playerAnimTimer = 0f;
                    playerCurrentFrame = (playerCurrentFrame + 1) % playerTotalFrames;
                }
            }
            else { playerCurrentFrame = 0; }

            playerPos.X = Math.Clamp(playerPos.X, 20, 1260);
            playerPos.Y = Math.Clamp(playerPos.Y, 20, 700);

            // 2. Bullet Spawning & Movement
            if (primaryMouse != null && !isWaveEnding)
            {
                Vector2 mousePos = primaryMouse.Position;
                if (mousePos.X >= 0 && mousePos.X <= 1280 && mousePos.Y >= 0 && mousePos.Y <= 720)
                {
                    Vector2 aimDir = mousePos - playerPos;
                    if (aimDir != Vector2.Zero) aimDir = Vector2.Normalize(aimDir);
                    else aimDir = new Vector2(0, -1); 

                    fireTimer += (float)delta;
                    if (fireTimer >= fireRate) { fireTimer = 0f; bullets.Add(new Projectile { Position = playerPos, Direction = aimDir }); }
                }
            }

            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                bullets[i].Position += bullets[i].Direction * bulletSpeed * (float)delta;
                
                // Bullet vs Obstacle Collision
                bool hitObstacle = false;
                foreach(var s in obstacles)
                {
                    if (bullets[i].Position.X > s.Hitbox.Origin.X && bullets[i].Position.X < s.Hitbox.Origin.X + s.Hitbox.Size.X &&
                        bullets[i].Position.Y > s.Hitbox.Origin.Y && bullets[i].Position.Y < s.Hitbox.Origin.Y + s.Hitbox.Size.Y)
                    {
                        hitObstacle = true; break;
                    }
                }
                
                if (hitObstacle) { bullets.RemoveAt(i); continue; }

                var pos = bullets[i].Position;
                if (pos.X < -100 || pos.X > 1380 || pos.Y < -100 || pos.Y > 820) bullets.RemoveAt(i);
            }

            // ------------------------------------------------
            // 3. ENEMY & BOSS SPAWNING (WAVE LOGIC)
            // ------------------------------------------------
            if (enemiesToSpawnThisLevel > 0 && !isBossActive)
            {
                enemySpawnTimer += (float)delta;
                if (enemySpawnTimer >= enemySpawnRate)
                {
                    enemySpawnTimer = 0f; 
                    enemiesToSpawnThisLevel--;

                    // BOSS SPAWN LOGIC (Level 5)
                    if (currentLevel == 5) 
                    { 
                        isBossActive = true; 
                        bossHealth = 100; 

                        int bossSide = rand.Next(0, 4); 
                        float bx = 0, by = 0;
                        switch (bossSide) 
                        { 
                            case 0: bx = rand.Next(100, 1180); by = -150; break; // Top
                            case 1: bx = 1430; by = rand.Next(100, 620); break;  // Right
                            case 2: bx = rand.Next(100, 1180); by = 870; break;  // Bottom
                            case 3: bx = -150; by = rand.Next(100, 620); break;  // Left
                        }
                        bossPos = new Vector2(bx, by);
                    }
                    // REGULAR SLIME SPAWN LOGIC (Levels 1-4)
                    else
                    {
                        int side = rand.Next(0, 4); float spawnX = 0, spawnY = 0;
                        switch (side) 
                        { 
                            case 0: spawnX = rand.Next(-50, 1330); spawnY = -50; break; 
                            case 1: spawnX = 1330; spawnY = rand.Next(-50, 770); break; 
                            case 2: spawnX = rand.Next(-50, 1330); spawnY = 770; break; 
                            case 3: spawnX = -50; spawnY = rand.Next(-50, 770); break; 
                        }
                        enemies.Add(new Enemy { Position = new Vector2(spawnX, spawnY) });
                    }
                }
            }
            // CRUCIAL: WAVE END & SHOP TRANSITION LOGIC
            else if (enemies.Count == 0 && !isBossActive)
            {
                if (!isWaveEnding) { isWaveEnding = true; waveEndTimer = 7.0f; }
                else
                {
                    waveEndTimer -= (float)delta;
                    if (waveEndTimer <= 0 || gems.Count == 0)
                    {
                        isWaveEnding = false; gems.Clear();
                        if (currentLevel >= 5) { isGameOver = true; } // You won!
                        else { currentState = GameState.Shop; playerPos = new Vector2(640, 600); bullets.Clear(); }
                    }
                }
            }

            // 4. Enemy Movement & Animation
            // 4. Enemy Movement & Animation (With Sliding Resolution)
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                Vector2 direction = playerPos - enemies[i].Position;
                if (direction != Vector2.Zero) 
                {
                    direction = Vector2.Normalize(direction);
                    float moveAmount = enemySpeed * (float)delta;
                    
                    Vector2 nextPos = enemies[i].Position + (direction * moveAmount);
                    
                    // --- SMART SLIDING LOGIC ---
                    // 1. Try moving the full distance
                    var eRectFull = new Rectangle<int>(new Vector2D<int>((int)nextPos.X - 16, (int)nextPos.Y - 16), new Vector2D<int>(32, 32));
                    bool blockedFull = false;
                    foreach (var s in obstacles) { if (CheckCollision(eRectFull, s.Hitbox)) { blockedFull = true; break; } }

                    if (!blockedFull)
                    {
                        enemies[i].Position = nextPos;
                    }
                    else
                    {
                        // 2. If blocked, try moving ONLY on the X axis
                        Vector2 nextPosX = enemies[i].Position + new Vector2(direction.X * moveAmount, 0);
                        var eRectX = new Rectangle<int>(new Vector2D<int>((int)nextPosX.X - 16, (int)nextPosX.Y - 16), new Vector2D<int>(32, 32));
                        bool blockedX = false;
                        foreach (var s in obstacles) { if (CheckCollision(eRectX, s.Hitbox)) { blockedX = true; break; } }
                        
                        if (!blockedX) 
                        {
                            enemies[i].Position = nextPosX;
                        }
                        else
                        {
                            // 3. If X is blocked, try moving ONLY on the Y axis
                            Vector2 nextPosY = enemies[i].Position + new Vector2(0, direction.Y * moveAmount);
                            var eRectY = new Rectangle<int>(new Vector2D<int>((int)nextPosY.X - 16, (int)nextPosY.Y - 16), new Vector2D<int>(32, 32));
                            bool blockedY = false;
                            foreach (var s in obstacles) { if (CheckCollision(eRectY, s.Hitbox)) { blockedY = true; break; } }
                            
                            if (!blockedY) enemies[i].Position = nextPosY;
                        }
                    }

                    // --- ANIMATION ---
                    if (Math.Abs(direction.X) > Math.Abs(direction.Y)) enemies[i].CurrentRow = direction.X > 0 ? 3 : 2;
                    else enemies[i].CurrentRow = direction.Y > 0 ? 0 : 1;
                        
                    enemies[i].AnimTimer += (float)delta;
                    if (enemies[i].AnimTimer >= enemyAnimSpeed)
                    {
                        enemies[i].AnimTimer = 0f;
                        enemies[i].CurrentFrame = (enemies[i].CurrentFrame + 1) % enemyWalkFrames;
                    }
                }
            }

            
            // --- BOSS MOVEMENT & SPAWNING ---
            if (isBossActive)
            {
                // --- 1. NEW: Boss Animation Logic ---
                bossAnimTimer += (float)delta;
                if (bossAnimTimer > 0.1f) // Speed of the animation (0.1 seconds per frame)
                {
                    bossCurrentFrame++;
                    
                    if (bossCurrentFrame >= 8) bossCurrentFrame = 0; 
                    bossAnimTimer = 0f;
                }

                // --- 2. EXISTING: Boss Movement & Sliding Collision ---
                Vector2 direction = playerPos - bossPos;
                if (direction != Vector2.Zero) 
                {
                    direction = Vector2.Normalize(direction);
                    float bossMoveAmount = 80f * (float)delta;
                    Vector2 nextBossPos = bossPos + (direction * bossMoveAmount);

                    // Boss Sliding Collision Logic
                    int bSize = 64; 
                    int bHalf = bSize / 2;

                    var bRectFull = new Rectangle<int>(new Vector2D<int>((int)nextBossPos.X - bHalf, (int)nextBossPos.Y - bHalf), new Vector2D<int>(bSize, bSize));
                    bool blockedFull = false;
                    foreach (var s in obstacles) { if (CheckCollision(bRectFull, s.Hitbox)) { blockedFull = true; break; } }

                    if (!blockedFull) bossPos = nextBossPos;
                    else
                    {
                        // Try sliding on X
                        Vector2 nextPosX = bossPos + new Vector2(direction.X * bossMoveAmount, 0);
                        var bRectX = new Rectangle<int>(new Vector2D<int>((int)nextPosX.X - bHalf, (int)nextPosX.Y - bHalf), new Vector2D<int>(bSize, bSize));
                        bool blockedX = false;
                        foreach (var s in obstacles) { if (CheckCollision(bRectX, s.Hitbox)) { blockedX = true; break; } }
                        
                        if (!blockedX) bossPos = nextPosX;
                        else
                        {
                            // Try sliding on Y
                            Vector2 nextPosY = bossPos + new Vector2(0, direction.Y * bossMoveAmount);
                            var bRectY = new Rectangle<int>(new Vector2D<int>((int)nextPosY.X - bHalf, (int)nextPosY.Y - bHalf), new Vector2D<int>(bSize, bSize));
                            bool blockedY = false;
                            foreach (var s in obstacles) { if (CheckCollision(bRectY, s.Hitbox)) { blockedY = true; break; } }
                            
                            if (!blockedY) bossPos = nextPosY;
                        }
                    }
                }

                // --- 3. EXISTING: Safe Minion Spawning Logic ---
                if (rand.Next(0, 100) < 2) 
                {
                    // Offset the spawn slightly so they drop out from under the boss
                    Vector2 spawnPos = new Vector2(bossPos.X, bossPos.Y + 20);
                    
                    // Predict the minion's 32x32 hitbox
                    var spawnRect = new Rectangle<int>(new Vector2D<int>((int)spawnPos.X - 16, (int)spawnPos.Y - 16), new Vector2D<int>(32, 32));
                    
                    // Only spawn if the area is NOT inside a wall
                    bool canSpawn = true;
                    foreach (var s in obstacles)
                    {
                        if (CheckCollision(spawnRect, s.Hitbox)) { canSpawn = false; break; }
                    }

                    if (canSpawn) 
                    {
                        enemies.Add(new Enemy { Position = spawnPos });
                    }
                }
            }

            // 5. Collisions (Bullets hitting Enemies)
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                bool bulletDestroyed = false;
                if (isBossActive && Vector2.Distance(bullets[i].Position, bossPos) < 60f)
                {
                    bossHealth--; bulletDestroyed = true;
                    if (bossHealth <= 0) { isBossActive = false; for(int g=0; g<20; g++) gems.Add(new Vector2(bossPos.X + rand.Next(-50, 50), bossPos.Y + rand.Next(-50, 50))); }
                }
                if (!bulletDestroyed)
                {
                    for (int j = enemies.Count - 1; j >= 0; j--)
                    {
                        if (Vector2.Distance(bullets[i].Position, enemies[j].Position) < 20f)
                        {
                            gems.Add(new Vector2(enemies[j].Position.X, enemies[j].Position.Y)); 
                            enemies.RemoveAt(j); 
                            bulletDestroyed = true; 
                            break;
                        }
                    }
                }
                if (bulletDestroyed) bullets.RemoveAt(i);
            }

            // 6. Collisions (Player taking damage)
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (Vector2.Distance(playerPos, enemies[i].Position) < 30f)
                {
                    if (shieldHealth > 0) { shieldHealth -= 20; if (shieldHealth < 0) { playerHealth += shieldHealth; shieldHealth = 0; } } else { playerHealth -= 20; }
                    if (playerHealth <= 0) isGameOver = true;
                    enemies.RemoveAt(i);
                }
            }

            if (isBossActive && Vector2.Distance(playerPos, bossPos) < 60f)
            {
                if (shieldHealth > 0) { shieldHealth -= 50; if (shieldHealth < 0) { playerHealth += shieldHealth; shieldHealth = 0; } } else { playerHealth -= 50; }
                if (playerHealth <= 0) isGameOver = true;
                playerPos.Y += 100; 
            }

            // 7. Gem Pickup
            for (int i = gems.Count - 1; i >= 0; i--)
            {
                if (Vector2.Distance(playerPos, gems[i]) < pickupRadius) { gems.RemoveAt(i); gemsCurrency++; }
            }
        }
        
        if (isWaveEnding) window.Title = $"HURRY! Time remaining: {Math.Round(waveEndTimer, 1)}s | Gems: {gemsCurrency}";
        else if (currentState == GameState.Playing) window.Title = $"Level: {currentLevel} | Gems: {gemsCurrency} | HP: {playerHealth}/{playerMaxHealth} | Shield: {shieldHealth}/{maxShield}";
    };

    window.Render += (delta) =>
    {
        // ----------------------------------------------------
        // 1. MENU, PAUSE, & GAMEOVER (These return early)
        // ----------------------------------------------------
       if (currentState == GameState.MainMenu)
    {
        // 1. Draw Background
        var bgRect = new Rectangle<int>(new Vector2D<int>(0, 0), new Vector2D<int>(1280, 720));
        if (texMenuBg != null) sdl.RenderCopy(renderer, texMenuBg, null, ref bgRect);
        else { sdl.SetRenderDrawColor(renderer, 10, 40, 70, 255); sdl.RenderClear(renderer); }

        // 2. Slime Button Dimensions
        // Making it shorter horizontally (500 -> 450) and taller (180 -> 220) 
        // to prevent the "squashed" look and the drip clipping.
        int btnW = 400; 
        int btnH = 220; 
        int btnX = (1280 / 2) - (btnW / 2); // 415
        int btnY = 320; 
        var playBtnRect = new Rectangle<int>(new Vector2D<int>(btnX, btnY), new Vector2D<int>(btnW, btnH));

        // 3. Draw the Slime
        if (texPlayBtn != null)
        {
            sdl.RenderCopy(renderer, texPlayBtn, null, ref playBtnRect);
        }
        else
        {
            sdl.SetRenderDrawColor(renderer, 50, 200, 50, 255);
            sdl.RenderFillRect(renderer, ref playBtnRect);
        }

        // 4. FIXING THE TEXT ALIGNMENT
        // We are moving it further RIGHT (560) and further DOWN (400)
        sdl.SetRenderDrawColor(renderer, 20, 50, 20, 255); 
        DrawText("PLAY", 600, 400, 5); 

        sdl.RenderPresent(renderer); 
        return;
    }
        if (isGameOver)
        {
            sdl.SetRenderDrawColor(renderer, 100, 0, 0, 255); sdl.RenderClear(renderer); sdl.RenderPresent(renderer); return; 
        }
        if (currentState == GameState.Paused)
        {
            // Dark screen overlay
            sdl.SetRenderDrawColor(renderer, 0, 0, 0, 200); 
            var fullScreen = new Rectangle<int>(new Vector2D<int>(0, 0), new Vector2D<int>(1280, 720));
            sdl.RenderFillRect(renderer, ref fullScreen);
            
            // Draw the flat gray Pause Box
            sdl.SetRenderDrawColor(renderer, 50, 50, 50, 255); 
            var pauseBox = new Rectangle<int>(new Vector2D<int>(240, 260), new Vector2D<int>(800, 200));
            sdl.RenderFillRect(renderer, ref pauseBox);
            
            // Draw text
            sdl.SetRenderDrawColor(renderer, 255, 255, 255, 255); // White font
            DrawText("PAUSED", 571, 290, 6); // 571 is exactly in the middle!
            
            sdl.SetRenderDrawColor(renderer, 200, 200, 200, 255); // Light Gray font
            DrawText("[R] RESTART  [M] MENU  [Q] QUIT", 455, 390, 3); 
            
            sdl.RenderPresent(renderer); return;
        }

        // ----------------------------------------------------
        // 2. ENVIRONMENT RENDERING (Shop OR Playing)
        // ----------------------------------------------------
        if (currentState == GameState.Shop)
        {
            if (texShopBg != null) 
            {
                int bgTileSize = 128; // You can change this to 64 or 256 to make the grass bigger/smaller
                for (int x = 0; x < 1280; x += bgTileSize)
                {
                    for (int y = 0; y < 720; y += bgTileSize)
                    {
                        var bgDest = new Rectangle<int>(new Vector2D<int>(x, y), new Vector2D<int>(bgTileSize, bgTileSize));
                        // Take a perfect 1:1 square slice so it never squishes
                        var bgSrc = new Rectangle<int>(new Vector2D<int>(0, 0), new Vector2D<int>(bgTileSize, bgTileSize)); 
                        
                        sdl.RenderCopy(renderer, texShopBg, ref bgSrc, ref bgDest);
                    }
                }
            }
            else 
            {
                // Fallback color
                var shopBgRect = new Rectangle<int>(new Vector2D<int>(0, 0), new Vector2D<int>(1280, 720));
                sdl.SetRenderDrawColor(renderer, 20, 20, 40, 255); 
                sdl.RenderFillRect(renderer, ref shopBgRect);
            }

            // 2. Draw Weapon Upgrade (The "Upgrade Ability" Hut - Wider)
            var weaponPad = new Rectangle<int>(new Vector2D<int>(150, 200), new Vector2D<int>(250, 220));
            if (texWeaponPad != null) 
                sdl.RenderCopy(renderer, texWeaponPad, null, ref weaponPad);
            else { sdl.SetRenderDrawColor(renderer, 255, 100, 100, 255); sdl.RenderFillRect(renderer, ref weaponPad); }

            // 3. Draw Shield Upgrade (The "Shield Shop" Hut - Taller)
            var shieldPad = new Rectangle<int>(new Vector2D<int>(850, 170), new Vector2D<int>(200, 260));
            if (texShieldPad != null) 
                sdl.RenderCopy(renderer, texShieldPad, null, ref shieldPad);
            else { sdl.SetRenderDrawColor(renderer, 100, 255, 255, 255); sdl.RenderFillRect(renderer, ref shieldPad); }

            // 4. Draw Exit Door (The "Arena" Wooden Sign)
            var exitDoor = new Rectangle<int>(new Vector2D<int>(560, 20), new Vector2D<int>(120, 120));
            if (texExitDoor != null) 
                sdl.RenderCopy(renderer, texExitDoor, null, ref exitDoor);
            else { sdl.SetRenderDrawColor(renderer, 100, 255, 100, 255); sdl.RenderFillRect(renderer, ref exitDoor); }
        
            
        }
        else if (currentState == GameState.Playing)
        { 
            sdl.SetRenderDrawColor(renderer, 0, 0, 0, 255); 
            sdl.RenderClear(renderer);

            
            // RENDER THE TILE MAP 
            for (int x = 0; x < mapColumns; x++)
            {
                for (int y = 0; y < mapRows; y++)
                {
                    int tileID = tileMap[x, y];
                    
                    // Where to draw on the screen:
                    var destRect = new Rectangle<int>(new Vector2D<int>(x * tileSize, y * tileSize), new Vector2D<int>(tileSize, tileSize));

                    if (texTileset != null && tileID == 0) 
                    {
                       
                        sdl.RenderCopy(renderer, texTileset, null, ref destRect);
                    }
                    else 
                    {
                        // Fallback color
                        sdl.SetRenderDrawColor(renderer, 34, 139, 34, 255); 
                        sdl.RenderFillRect(renderer, ref destRect);
                    }
                }
            }

            // Draw Structures (Obstacles)
            foreach (var s in obstacles)
            {
                var rect = s.Hitbox;
                if (texStructure != null) sdl.RenderCopy(renderer, texStructure, null, ref rect);
                else { sdl.SetRenderDrawColor(renderer, 100, 100, 100, 255); sdl.RenderFillRect(renderer, ref rect); }
            }
            
            // Draw Bullets
            foreach (var bullet in bullets) { var bRect = new Rectangle<int>(new Vector2D<int>((int)bullet.Position.X - 4, (int)bullet.Position.Y - 4), new Vector2D<int>(8, 8)); if (texBullet != null) sdl.RenderCopy(renderer, texBullet, null, ref bRect); else { sdl.SetRenderDrawColor(renderer, 255, 255, 0, 255); sdl.RenderFillRect(renderer, ref bRect); } }
            
            // Draw Boss
           
            // Draw Boss
            if (isBossActive) 
            { 
                
                var bossDest = new Rectangle<int>(new Vector2D<int>((int)bossPos.X - 60, (int)bossPos.Y - 60), new Vector2D<int>(120, 120)); 
                
                if (texBoss != null) 
                {
                    // 2. THE FIX: Cut out the current animation frame from the spritesheet
                    var bossSrc = new Rectangle<int>(
                        new Vector2D<int>(bossCurrentFrame * bossFrameWidth, bossCurrentRow * bossFrameHeight), 
                        new Vector2D<int>(bossFrameWidth, bossFrameHeight)
                    );
                    
                    
                    sdl.RenderCopy(renderer, texBoss, ref bossSrc, ref bossDest);
                }
                else 
                {
                    // Fallback purple box if the image fails to load
                    sdl.SetRenderDrawColor(renderer, 150, 0, 200, 255); 
                    sdl.RenderFillRect(renderer, ref bossDest); 
                }

                // 3. Draw the Boss Health Bar (Floating above)
                sdl.SetRenderDrawColor(renderer, 255, 0, 0, 255); 
                var bossHpRect = new Rectangle<int>(new Vector2D<int>((int)bossPos.X - 60, (int)bossPos.Y - 80), new Vector2D<int>((int)(120 * (bossHealth / 100f)), 10)); 
                sdl.RenderFillRect(renderer, ref bossHpRect); 
            }
            
            // Draw Animated Enemies
            foreach (var enemy in enemies) 
            { 
                int drawnWidth = (int)(enemyFrameWidth * enemyScale);
                int drawnHeight = (int)(enemyFrameHeight * enemyScale);
                var eDest = new Rectangle<int>(new Vector2D<int>((int)enemy.Position.X - (drawnWidth / 2), (int)enemy.Position.Y - (drawnHeight / 2)), new Vector2D<int>(drawnWidth, drawnHeight)); 
                
                if (texEnemy != null) 
                {
                    var eSrc = new Rectangle<int>(new Vector2D<int>(enemy.CurrentFrame * enemyFrameWidth, enemy.CurrentRow * enemyFrameHeight), new Vector2D<int>(enemyFrameWidth, enemyFrameHeight));
                    sdl.RenderCopy(renderer, texEnemy, ref eSrc, ref eDest); 
                }
                else { sdl.SetRenderDrawColor(renderer, 255, 0, 0, 255); sdl.RenderFillRect(renderer, ref eDest); } 
            }
            
            // Draw Gems
            foreach (var gem in gems) 
            { 
                int drawnGemWidth = (int)(gemFrameWidth * gemScale);
                int drawnGemHeight = (int)(gemFrameHeight * gemScale);
                var gDest = new Rectangle<int>(new Vector2D<int>((int)gem.X - (drawnGemWidth / 2), (int)gem.Y - (drawnGemHeight / 2)), new Vector2D<int>(drawnGemWidth, drawnGemHeight)); 
                
                if (texGem != null) sdl.RenderCopy(renderer, texGem, null, ref gDest); 
                else { sdl.SetRenderDrawColor(renderer, 0, 255, 100, 255); sdl.RenderFillRect(renderer, ref gDest); } 
            }
        } 

        // ----------------------------------------------------
        // 3. SHARED RENDERING (Player & HUD draw everywhere)
        // ----------------------------------------------------
        
        // Draw Animated Player
        int pDrawnW = (int)(playerFrameWidth * playerScale);
        int pDrawnH = (int)(playerFrameHeight * playerScale);
        var pDest = new Rectangle<int>(new Vector2D<int>((int)playerPos.X - (pDrawnW / 2), (int)playerPos.Y - (pDrawnH / 2)), new Vector2D<int>(pDrawnW, pDrawnH));

        if (texPlayer != null) 
        {
            var pSrc = new Rectangle<int>(new Vector2D<int>(playerCurrentFrame * playerFrameWidth, 0), new Vector2D<int>(playerFrameWidth, playerFrameHeight));
            sdl.RenderCopyEx(renderer, texPlayer, ref pSrc, ref pDest, 0, null, playerFlip); 
        } 
        else 
        {
            sdl.SetRenderDrawColor(renderer, 0, 120, 255, 255); 
            sdl.RenderFillRect(renderer, ref pDest); 
        }

        // Draw Player Shield
        if (shieldHealth > 0) { byte shieldAlpha = (byte)(255 * ((float)shieldHealth / maxShield)); sdl.SetRenderDrawColor(renderer, 0, 255, 255, shieldAlpha); var shieldRect = new Rectangle<int>(new Vector2D<int>((int)playerPos.X - 25, (int)playerPos.Y - 25), new Vector2D<int>(50, 50)); sdl.RenderDrawRect(renderer, ref shieldRect); }

        // Draw HUD
        if (!isGameOver && currentState != GameState.MainMenu)
        {
            // --- 1. HP Bar ---
           // --- ON-SCREEN HUD ---
        if (!isGameOver && currentState != GameState.MainMenu)
        {
            // 1. HP & Shield Bars
            sdl.SetRenderDrawColor(renderer, 50, 0, 0, 255); var hudHpBg = new Rectangle<int>(new Vector2D<int>(20, 20), new Vector2D<int>(200, 20)); sdl.RenderFillRect(renderer, ref hudHpBg);
            sdl.SetRenderDrawColor(renderer, 255, 0, 0, 255); int currentHpWidth = (int)(200 * (Math.Max(0, playerHealth) / (float)playerMaxHealth)); var hudHpFg = new Rectangle<int>(new Vector2D<int>(20, 20), new Vector2D<int>(currentHpWidth, 20)); sdl.RenderFillRect(renderer, ref hudHpFg);
            
            sdl.SetRenderDrawColor(renderer, 0, 50, 50, 255); var hudShieldBg = new Rectangle<int>(new Vector2D<int>(20, 45), new Vector2D<int>(200, 10)); sdl.RenderFillRect(renderer, ref hudShieldBg);
            sdl.SetRenderDrawColor(renderer, 0, 255, 255, 255); int currentShieldWidth = (int)(200 * (Math.Max(0, shieldHealth) / (float)maxShield)); var hudShieldFg = new Rectangle<int>(new Vector2D<int>(20, 45), new Vector2D<int>(currentShieldWidth, 10)); sdl.RenderFillRect(renderer, ref hudShieldFg);
            
            // 2. Gem Icon & Counter (Top Left)
            var hudGem = new Rectangle<int>(new Vector2D<int>(20, 60), new Vector2D<int>(15, 15)); 
            if (texGem != null) sdl.RenderCopy(renderer, texGem, null, ref hudGem);
            else { sdl.SetRenderDrawColor(renderer, 0, 255, 100, 255); sdl.RenderFillRect(renderer, ref hudGem); }
            
            // Draw Gem Number
            sdl.SetRenderDrawColor(renderer, 255, 255, 255, 255);
            DrawText(gemsCurrency.ToString(), 45, 60, 3);

            // 3. Level Indicator (Top Right)
            DrawText(currentLevel.ToString(), 1220, 20, 4);

            // 4. Wave End Panic Timer (Center Screen)
            if (isWaveEnding)
            {
                sdl.SetRenderDrawColor(renderer, 255, 50, 50, 255); 
                int timeLeft = (int)Math.Ceiling(waveEndTimer); 
                DrawText(timeLeft.ToString(), 620, 100, 8); 
            }
        }
            else
            {
                // Fallback text if the UI sheet fails to load
                sdl.SetRenderDrawColor(renderer, 255, 255, 255, 255);
                DrawText($"HP: {playerHealth} / SHIELD: {shieldHealth}", 20, 20, 3);
            }
        }
        
        // ----------------------------------------------------
        // 4. PUSH TO SCREEN (This was trapped!)
        // ----------------------------------------------------
        sdl.RenderPresent(renderer);
    };

    window.Run();

    if (texPlayer != null) sdl.DestroyTexture(texPlayer);
    if (texEnemy != null) sdl.DestroyTexture(texEnemy);
    if (texBullet != null) sdl.DestroyTexture(texBullet);
    if (texGem != null) sdl.DestroyTexture(texGem);
    if (renderer != null) sdl.DestroyRenderer(renderer);
}


class Projectile
{
    public Vector2 Position;
    public Vector2 Direction;
}

class Enemy
{
    public Vector2 Position;
    public int CurrentFrame = 0;
    public int CurrentRow = 0;
    public float AnimTimer = 0f;
}

class Structure
{
    public Rectangle<int> Hitbox;
    
}

enum GameState { MainMenu, Playing, Paused, Shop, GameOver }