{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
    git-temp-commit = {
      url = "git+https://codeberg.org/svenvanheugten/git-temp-commit.git?ref=main";
      inputs.nixpkgs.follows = "nixpkgs";
      inputs.flake-utils.follows = "flake-utils";
    };
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
      git-temp-commit,
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
          src = ./Mutannot;
          projectFile = "Mutannot.fsproj";
          nugetDeps = ./Mutannot/deps.json;
          executables = [ "mutannot" ];
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-sdk_10;
          useDotnetFromEnv = true;

          meta = {
            mainProgram = "mutannot";
          };
        };

        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.git
            pkgs.dotnet-sdk_10
            git-temp-commit.packages.${system}.default
          ];
        };
      }
    );
}
