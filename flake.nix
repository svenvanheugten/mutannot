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
          version = "0.1.0";
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
          ];

          preCheck = ''
            git init
            git add .
            git -c user.email="nix@build" -c user.name="Nix" commit -m "init"
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
