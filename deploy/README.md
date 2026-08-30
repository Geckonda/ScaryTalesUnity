# Deploying the ScaryTales server

The server is a headless Unity process speaking **KCP — raw UDP on port 7777**.
It is not HTTP. There is no reverse proxy, no NGINX, no TLS termination, and
nothing to configure in your existing web stack: the game and a website on the
same machine never meet, because they are different protocols on different
ports.

## 1. Build the server

In Unity Hub, add the module **Linux Dedicated Server Build Support** (it pulls
in Linux Build Support as a dependency).

`File → Build Settings`:

- **Platform:** Dedicated Server, **Target Platform:** Linux
- Scene list: **GameScene first**. `MenuScene` has no game table and Mirror's
  scene switching is off, so a build starting there joins a room and then shows
  nothing.
- Build into `deploy/build/`.

You get the player executable, `<Name>_Data/`, `UnityPlayer.so` and
`MonoBleedingEdge/`. Copy the whole folder — the binary alone does nothing.

The executable's name does not matter: Unity spells Linux builds either `Name`
or `Name.x86_64` depending on version and target, so the Dockerfile locates it
by the `<Name>_Data` folder beside it and accepts either. What *does* matter is
that these files sit **directly** in `deploy/build/`, not in a nested folder —
if the build fails, the image prints the contents of `/app` so you can see
which it is.

> A plain **Linux** build works too; the Dockerfile passes `-batchmode
> -nographics`, which makes Unity headless either way. The Dedicated Server
> target is just smaller and has no graphics code at all.

## 2. Set the client's address

`serverAddress` on `MenuManager` is baked into the client build, so point it at
your domain and rebuild the *clients* (not the server). A domain is fine — KCP
resolves hostnames via `Dns.GetHostAddresses`.

Before a public build, clear the two local-testing values, or every first room
is called `LOCALHOST` and an empty join box silently walks into a stranger's
game:

| Component | Field | Set to |
| :--- | :--- | :--- |
| `GameConnectionManager` | `_fixedRoomCode` | *(empty)* |
| `MenuManager` | `fallbackRoomCode` | *(empty)* |
| `MenuManager` | `serverAddress` | your domain |

`hostLocallyOnCreate` must stay **off** — with a real server, creating a room
is an ordinary client action.

## 3. Run it

```sh
scp -r deploy/ user@your-server:/opt/scarytales/
ssh user@your-server
cd /opt/scarytales
docker compose up -d --build
docker compose logs -f
```

Expected on a healthy start:

```
[Server] Starting as a dedicated server (no local player).
Server listening on port 7777
```

## 4. Open the port

```sh
sudo ufw allow 7777/udp
```

If your provider has its own firewall or security group, open **UDP 7777**
there too. Most cloud panels default to TCP-only rules, and a TCP rule on a UDP
game fails silently.

## 5. Check it

From your own machine:

```sh
nc -u -z -v your-domain 7777      # UDP has no handshake, so this is weak evidence
```

The real test is a client: build one with `serverAddress` set to the domain,
press **Create Room**, and watch the server log for
`[Server] Room ... created with code ...`.

## Sizing

Modest. The whole game state is a handful of dictionaries per room, and a turn
produces a few dozen bytes of traffic.

| | |
| :--- | :--- |
| CPU | 1 vCPU is plenty |
| RAM | 512 MB ceiling; real usage is well under it |
| Disk | ~250 MB for the build |
| Network | negligible — id-sized messages, no assets ever transferred |

One caveat worth knowing before you put it next to something else: the process
**ticks 60 times a second even with nobody playing**, because
`GameConnectionManager.Start` sets `Application.targetFrameRate = sendRate` and
`sendRate` is 60. That is a few percent of a core, permanently. A card game has
no need of 60 Hz — dropping `sendRate` to 20–30 on the `NetworkManager` roughly
halves it and changes nothing a player can feel.

## Operating notes

- `restart: unless-stopped` brings the server back after a crash or a host
  reboot. It does **not** preserve games in progress: rooms live in memory, so a
  restart drops every match. There is no persistence and none is planned.
- `docker compose logs -f` is the only window into what rooms exist. Watch for
  `(N live)` in room-creation lines — that count is how you know rooms are being
  freed rather than accumulating.
- Updating: rebuild in Unity, re-copy `deploy/build/`, then
  `docker compose up -d --build`. Anyone mid-game is disconnected and bounced to
  their menu, which is handled but not graceful.
