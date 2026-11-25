# ">" instead of tab as recipe prefix, so copy & paste cannot break tabs
.RECIPEPREFIX = >

PROJECT := $(notdir $(CURDIR))
DIST    := $(HOME)/dist/$(PROJECT)

# Inside the container (/.dockerenv exists) run directly, on the host via devcontainer exec
IN_CONTAINER := $(wildcard /.dockerenv)
RUN := $(if $(IN_CONTAINER),,devcontainer exec --workspace-folder . )

.PHONY: up build test dev publish

up:
> $(if $(IN_CONTAINER),@true,devcontainer up --workspace-folder . > /dev/null)

# ===== project-specific =====
# WPF app: built on Linux via EnableWindowsTargeting, runs only on Windows.
WIN   := -p:EnableWindowsTargeting=true
OUT   := build/win-x64/*
# Test projects targeting net10.0-windows (App, Viewport) need WPF at runtime and only run on Windows.
LINUX_TESTS := $(filter-out %Formwerk.Tests.App.csproj %Formwerk.Tests.Viewport.csproj,$(wildcard tests/*/*.csproj))

build: up
> $(RUN)dotnet build Formwerk.sln -c Release $(WIN) --nologo -v minimal
> $(RUN)dotnet publish src/Formwerk.App/Formwerk.App.csproj -c Release -r win-x64 --self-contained true -o build/win-x64 -p:DebugType=None -p:DebugSymbols=false $(WIN) --nologo -v minimal

# One test project at a time: the speed tests measure wall-clock time.
test: up
> $(RUN)bash -c 'rc=0; for p in $(LINUX_TESTS); do dotnet test "$$p" -c Release $(WIN) --nologo -v minimal || rc=1; done; exit $$rc'

dev: up
> @echo "Formwerk is a WPF app and cannot run on the server. Use 'make publish' and start Formwerk.exe on Windows."
# ============================

publish: build
> rm -rf $(DIST) && mkdir -p $(DIST)
> cp -r $(OUT) $(DIST)/
> @echo "Done: $(DIST)"
