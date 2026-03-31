#pragma once

#include <Arduino.h>

enum class BeepPattern {
  None,
  SingleShort,
  ArmConfirm,
  DisarmConfirm
};

struct DebouncedInput {
  uint8_t pin;
  unsigned long debounceMs;
  int stableState;
  int lastRawState;
  unsigned long lastChangeMs;
  void (*onStableChange)(int);
};

struct SystemState {
  bool armed = false;
  bool alarmActive = false;
  unsigned long eventCounter = 0;
  unsigned long lastHeartbeatMs = 0;
  unsigned long lastMqttAttemptMs = 0;
};

struct SirenState {
  int frequency = 1500;
  bool rampUp = true;
  unsigned long lastSirenStepMs = 0;

  BeepPattern activePattern = BeepPattern::None;
  bool toneOn = false;
  uint8_t stepIndex = 0;
  unsigned long stepStartedMs = 0;
  int currentToneFrequency = 0;
  unsigned long currentStepDurationMs = 0;
  unsigned long pendingPauseMs = 0;
};
