TARGET := iphone:clang:16.5:14.0
ARCHS = arm64

include $(THEOS)/makefiles/common.mk

TWEAK_NAME = eggupdate
eggupdate_FILES = Tweak.x
eggupdate_CFLAGS = -fobjc-arc
eggupdate_FRAMEWORKS = Foundation CoreFoundation

# Phase-2 install is withheld until the selector is confirmed on-device. To arm it after confirmation:
#   make EGGUPDATE_ARMED=1
ifeq ($(EGGUPDATE_ARMED),1)
eggupdate_CFLAGS += -DEGGUPDATE_ARMED=1
endif

include $(THEOS)/makefiles/tweak.mk
