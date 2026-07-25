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
          version = "0.0.0";
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

          nativeBuildInputs = [
            pkgs.git
            pkgs.fantomas
          ];

          doCheck = true;
          testProjectFile = [
            "Mutannot.UnitTests/Mutannot.UnitTests.fsproj"
            "Example.FSharp.Tests/Example.FSharp.Tests.fsproj"
            "Example.CSharp.Tests/Example.CSharp.Tests.csproj"
            "Mutannot.IntegrationTests/Mutannot.IntegrationTests.fsproj"
          ];

          preCheck = ''
            fantomas --check .
            git init
            git add .
            git -c user.email="nix@build" -c user.name="Nix" commit -m "init"
          '';

          # Turn the freshly installed mutannot onto the example projects, and onto
          # its own integration tests.
          postFixup = ''
            $out/bin/mutannot run Example.FSharp.Tests/Example.FSharp.Tests.fsproj
            $out/bin/mutannot run Example.CSharp.Tests/Example.CSharp.Tests.csproj
            $out/bin/mutannot run Mutannot.IntegrationTests/Mutannot.IntegrationTests.fsproj
          '';

          meta = {
            mainProgram = "mutannot";
          };
        };

        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.git
            pkgs.fantomas
            pkgs.dotnet-sdk_10
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
