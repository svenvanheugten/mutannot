{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
      in
      {
        packages.default = pkgs.buildDotnetModule {
          pname = "mutannot";
          version = "0.4.0";
          src = ./.;
          projectFile = [
            "Mutannot/Mutannot.fsproj"
          ];
          nugetDeps = ./deps.json;
          executables = [ "mutannot" ];
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-sdk_10;
          useDotnetFromEnv = true;

          # Fix for hanging builds
          MSBUILDDISABLENODEREUSE = 1;

          nativeBuildInputs = [ pkgs.git ];

          doCheck = true;
          testProjectFile = [
            "Mutannot.Tests/Mutannot.Tests.fsproj"
            "Mutannot.IntegrationTests/Mutannot.IntegrationTests.fsproj"
          ];

          preCheck = ''
            git init
            git add .
            git -c user.email="nix@build" -c user.name="Nix" commit -m "init"
          '';

          # Turn the freshly installed mutannot on its own integration tests.
          # The bin/ wrapper is created in fixup, after installPhase, so run it here.
          postFixup = ''
            $out/bin/mutannot run Mutannot.IntegrationTests/Mutannot.IntegrationTests.fsproj
          '';

          meta = {
            mainProgram = "mutannot";
          };
        };

        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.git
            pkgs.dotnet-sdk_10
            pkgs.dotnet-sdk_11
            # We can't rely on the standard `nix-build -A fetch-deps`: it only fetches
            # mutannot's own dependencies, not those of the sample projects that the
            # integration tests build at runtime. This script restores everything in the
            # slnx and writes a combined deps.json instead.
            (pkgs.writeShellApplication {
              name = "update-deps-json";
              meta.description = "Update deps.json with all dependencies that appear in the slnx file.";
              text = ''
                dotnet restore --packages=packages mutannot.slnx
                ${pkgs.lib.getExe pkgs.nuget-to-json} packages > deps.json
              '';
            })
          ];
        };
      }
    );
}
