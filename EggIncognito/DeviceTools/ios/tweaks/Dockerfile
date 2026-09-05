FROM ubuntu:24.04

ARG THEOS_REF=9bc73406cf80b711ef4d02c15ff1dedc4478a275
ARG SDKS_REF=0222fd5413cf4b9af096f37b4621afa2688572f7
ARG TOOLCHAIN_TAG=test-210562a
ARG TOOLCHAIN_ARCH=x86_64
ARG IOS_SDK=iPhoneOS16.5.sdk

ENV THEOS=/opt/theos
ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        build-essential ca-certificates curl fakeroot git libtinfo6 libxml2 perl rsync unzip xz-utils zip \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/theos/theos.git "$THEOS" \
    && git -C "$THEOS" checkout "$THEOS_REF" \
    && git -C "$THEOS" submodule update --init --recursive \
    && rm -rf "$THEOS/.git"

RUN curl -fsSL --proto '=https' "https://github.com/L1ghtmann/llvm-project/releases/download/$TOOLCHAIN_TAG/iOSToolchain-$TOOLCHAIN_ARCH.tar.xz" \
    | tar -xJ -C "$THEOS/toolchain/" \
    && test -x "$THEOS/toolchain/linux/iphone/bin/clang" \
    && test -x "$THEOS/toolchain/linux/iphone/bin/ldid"

RUN curl -fsSL --proto '=https' "https://github.com/theos/sdks/archive/$SDKS_REF.tar.gz" \
    | tar -xz -C "$THEOS/sdks/" --strip-components=1 "sdks-$SDKS_REF/$IOS_SDK"

ENV HOME=/tmp
WORKDIR /src
ENTRYPOINT ["make"]
CMD ["clean", "package"]
