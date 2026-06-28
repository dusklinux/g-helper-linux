# G-Helper Linux package for NixOS.
#
# Wraps the prebuilt AOT binary with nix-ld library resolution so
# runtime-extracted native libs (SkiaSharp, HarfBuzz, etc.) work.
# Builds gpu-helper from vendored C source as a proper Nix binary.
{
  lib,
  stdenv,
  makeWrapper,
  autoPatchelfHook,
  # Runtime deps for ghelper (Avalonia/SkiaSharp/ICU/.NET AOT)
  fontconfig,
  freetype,
  icu,
  openssl,
  zlib,
  libGL,
  libX11,
  libXcursor,
  libXi,
  libXrandr,
  libxkbcommon,
  wayland,
  dbus,
  glib,
  expat,
  pipewire,
  libxcb,
  libxext,
  libxinerama,
  libxfixes,
  libxrender,
  libICE,
  libSM,
  # gpu-helper build dep
  glibc,
}:

let
  # Runtime library closure for ghelper + its extracted native helpers.
  # NIX_LD_LIBRARY_PATH makes these visible to runtime-extracted .so files
  # that can't be patchelf'd (they don't exist until the app unpacks them).
  runtimeLibs = lib.makeLibraryPath [
    fontconfig
    freetype
    icu
    openssl
    zlib
    libGL
    libX11
    libXcursor
    libXi
    libXrandr
    libxkbcommon
    wayland
    dbus
    glib
    expat
    pipewire
    libxcb
    libxext
    libxinerama
    libxfixes
    libxrender
    libICE
    libSM
    stdenv.cc.cc.lib # libstdc++
  ];

in
{
  # The main GUI binary, wrapped with nix-ld library paths.
  ghelper = stdenv.mkDerivation {
    pname = "ghelper";
    version = "1.0.0";

    # Prebuilt AOT binary from dist/ (run build.sh first)
    src = ../dist;

    # No autoPatchelfHook: .NET managed DLLs get corrupted by patchelf.
    # nix-ld + LD_LIBRARY_PATH handle native lib resolution at runtime.
    nativeBuildInputs = [ makeWrapper ];

    # .NET managed PE DLLs get corrupted by strip.
    dontStrip = true;
    dontPatchELF = true;
    buildInputs = [
      stdenv.cc.cc.lib # libstdc++
      zlib
      icu
      openssl
      fontconfig
    ];

    dontUnpack = true;

    installPhase = ''
      mkdir -p $out/bin

      if [ -f $src/ghelper.dll ]; then
        # Folder build: .NET host + managed DLLs must stay together.
        # nix-ld handles native lib resolution at runtime.
        mkdir -p $out/lib/ghelper
        cp -r $src/* $out/lib/ghelper/
        chmod +x $out/lib/ghelper/ghelper

        makeWrapper $out/lib/ghelper/ghelper $out/bin/ghelper \
          --prefix NIX_LD_LIBRARY_PATH : "${runtimeLibs}:/run/opengl-driver/lib" \
          --prefix LD_LIBRARY_PATH : "${runtimeLibs}:/run/opengl-driver/lib"
      else
        # AOT single-binary (may be UPX-compressed)
        install -m755 $src/ghelper $out/bin/ghelper

        wrapProgram $out/bin/ghelper \
          --prefix NIX_LD_LIBRARY_PATH : "${runtimeLibs}:/run/opengl-driver/lib" \
          --prefix LD_LIBRARY_PATH : "${runtimeLibs}:/run/opengl-driver/lib"
      fi

      # Desktop entry + icon (Exec=ghelper, resolved on PATH by the module).
      install -Dm644 ${../install/ghelper.desktop} \
        $out/share/applications/ghelper.desktop
      install -Dm644 ${../install/ghelper.png} \
        $out/share/icons/hicolor/256x256/apps/ghelper.png
    '';

    meta = {
      description = "G-Helper for Linux - ASUS/Lenovo laptop control utility";
      license = lib.licenses.gpl3;
      platforms = [ "x86_64-linux" ];
      mainProgram = "ghelper";
    };
  };

  # Privileged GPU helper, built from vendored C source.
  # Runs as root via sudo/pkexec - must be a native Nix binary
  # so the dynamic loader works without nix-ld in root context.
  gpu-helper = stdenv.mkDerivation {
    pname = "ghelper-gpu-helper";
    version = "1.0.0";

    src = ../vendor/gpu-helper;

    buildPhase = ''
      cc -O2 -Wall -o gpu-helper gpu-helper.c -ldl
    '';

    installPhase = ''
      mkdir -p $out/bin
      install -m755 gpu-helper $out/bin/gpu-helper
    '';

    meta = {
      description = "G-Helper privileged GPU operations helper";
      license = lib.licenses.gpl3;
      platforms = [ "x86_64-linux" ];
    };
  };

  # GPU block helper bash script. Handles modprobe block files,
  # udev remove rules, and boot triggers for GPU mode persistence.
  gpu-block-helper = stdenv.mkDerivation {
    pname = "ghelper-gpu-block-helper";
    version = "1.0.0";

    src = ../install/gpu-block-helper.sh;
    dontUnpack = true;

    installPhase = ''
      mkdir -p $out/bin
      install -m755 $src $out/bin/gpu-block-helper.sh
    '';

    meta = {
      description = "G-Helper GPU block helper script";
      license = lib.licenses.gpl3;
      platforms = [ "x86_64-linux" ];
    };
  };

  # GPU boot script: applies the pending GPU mode early in boot (firmware
  # dgpu_disable on ASUS, modprobe blacklist on PCI). Used by the optional
  # ghelper-gpu-boot.service. Standalone bash; calls gpu-helper/modprobe/udevadm.
  ghelper-gpu-boot = stdenv.mkDerivation {
    pname = "ghelper-gpu-boot";
    version = "1.0.0";

    src = ../install/ghelper-gpu-boot.sh;
    dontUnpack = true;

    installPhase = ''
      mkdir -p $out/bin
      install -m755 $src $out/bin/ghelper-gpu-boot.sh
    '';

    meta = {
      description = "G-Helper GPU boot mode applier";
      license = lib.licenses.gpl3;
      platforms = [ "x86_64-linux" ];
    };
  };
}
