#!/usr/bin/env bash
set -euo pipefail

# brew install openssl3
# brew install gcc

pushd Algorithms/pufferfish2bmb

rm pufferfish2-apple-m1.o pufferfish2-apple-m1.so pufferfish2 || true

case "$1" in
  gcc)
    gcc-11 \
      -O3 -march=native -funroll-loops \
      -I/opt/homebrew/Cellar/openssl@3/3.0.4/include \
      -o pufferfish2-apple-m1.o -c pufferfish2.cpp

    gcc-11 \
      -I/opt/homebrew/Cellar/openssl@3/3.0.4/include \
      -L/opt/homebrew/Cellar/openssl@3/3.0.4/lib \
      -lssl -lcrypto \
      -shared -o pufferfish2-apple-m1.so pufferfish2-apple-m1.o
  ;;
  clang)
    clang -O3 -funroll-loops \
      -I/opt/homebrew/Cellar/openssl@3/3.0.4/include \
      -o pufferfish2-apple-m1.o -c pufferfish2.cpp

    clang -I/opt/homebrew/Cellar/openssl@3/3.0.4/include -L/opt/homebrew/Cellar/openssl@3/3.0.4/lib -lssl -lcrypto -shared -o pufferfish2-apple-m1.so pufferfish2-apple-m1.o
  ;;
esac

cp pufferfish2-apple-m1.so pufferfish2

popd
dotnet run