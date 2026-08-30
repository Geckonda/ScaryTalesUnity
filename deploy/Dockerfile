# ScaryTales dedicated server.
#
# Expects a Linux build in ./build next to this file — see README.md.
#
# Note there is no web server here and no reason for one: the game speaks
# KCP, which is raw UDP on port 7777. Nothing about it is HTTP, so a reverse
# proxy would add a hop and a failure point and buy nothing.

FROM ubuntu:22.04

# Unity's Linux player needs almost nothing beyond libc, but it does want
# CA certificates present and a UTF-8 locale — card names are Russian, and
# the log is unreadable without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates locales \
    && locale-gen en_US.UTF-8 \
    && rm -rf /var/lib/apt/lists/*
ENV LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8

WORKDIR /app
COPY build/ /app/

# Find the player executable instead of demanding an exact filename.
#
# Unity names Linux builds inconsistently: depending on version and target it
# produces either `Name` or `Name.x86_64`, and it is easy to type a different
# name in the build dialog than the one written here. What *is* reliable is
# the `<Name>_Data` folder sitting beside the executable, so that is what we
# key off — then accept either spelling of the binary.
#
# Everything downstream uses the /app/server symlink, so nothing else has to
# care what Unity called it.
RUN set -eu; \
    data_dir="$(find /app -maxdepth 1 -type d -name '*_Data' -print -quit)"; \
    if [ -z "$data_dir" ]; then \
        echo "ERROR: no *_Data folder in /app."; \
        echo "The Unity build is not here, or it landed in a nested folder."; \
        echo "deploy/build/ must contain the binary, <Name>_Data/ and UnityPlayer.so directly."; \
        echo "--- what is actually in /app ---"; ls -la /app; \
        exit 1; \
    fi; \
    base="${data_dir%_Data}"; \
    if   [ -f "$base" ];         then bin="$base"; \
    elif [ -f "$base.x86_64" ];  then bin="$base.x86_64"; \
    else \
        echo "ERROR: found $data_dir but no executable beside it."; \
        echo "--- what is actually in /app ---"; ls -la /app; \
        exit 1; \
    fi; \
    echo "Using player executable: $bin"; \
    chmod +x "$bin"; \
    ln -s "$bin" /app/server; \
    # With --no-create-home the player logs "CreateDirectory '/home/...' failed"
    # and "Unable to load player prefs" on every start. Harmless here — nothing
    # in the project touches PlayerPrefs — but it is a real failure in the log,
    # and a log you have learned to ignore is a log that stops warning you.
    useradd --system --create-home --home-dir /home/scarytales scarytales; \
    chown -R scarytales /app

USER scarytales

EXPOSE 7777/udp

# -logFile /dev/stdout is what makes `docker logs` work. Without it Unity
# writes to ~/.config/unity3d/... inside the container, where nobody will
# ever look.
#
# -batchmode -nographics make an ordinary Linux build headless too, so this
# image works whether you built for "Dedicated Server" or plain Linux.
# -server is our own flag and forces the intent either way; see
# GameConnectionManager.WantsDedicatedServer.
ENTRYPOINT ["/app/server", "-server", "-batchmode", "-nographics", "-logFile", "/dev/stdout"]
