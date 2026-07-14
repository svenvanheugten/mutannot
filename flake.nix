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

          nativeBuildInputs = [ pkgs.git ];

          doCheck = true;
          testProjectFile = [
            "Mutannot.Tests/Mutannot.Tests.fsproj"
            "Mutannot.IntegrationTests/Mutannot.IntegrationTests.fsproj"
            "Example.CSharp.Tests/Example.CSharp.Tests.csproj"
            "Example.FSharp.Tests/Example.FSharp.Tests.fsproj"
            "Example.Mtp.Tests/Example.Mtp.Tests.csproj"
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
          ];
        };
      }
    );
}
