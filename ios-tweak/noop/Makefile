TARGET := iphone:clang:16.5:14.0
ARCHS = arm64

include $(THEOS)/makefiles/common.mk

TWEAK_NAME = eggnoop
eggnoop_FILES = Tweak.x
eggnoop_CFLAGS = -fobjc-arc

include $(THEOS)/makefiles/tweak.mk
