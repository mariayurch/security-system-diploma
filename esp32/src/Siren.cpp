#include "Siren.h"
#include <Arduino.h>
#include "Config.h"
#include "State.h"

struct PatternStep {
  int frequency;
  unsigned long durationMs;
  unsigned long pauseAfterMs;
};

static constexpr int BUZZER_LEDC_CHANNEL = 0;
static constexpr int BUZZER_LEDC_RESOLUTION = 8;

static constexpr PatternStep ARM_CONFIRM_STEPS[] = {
  {2000, 90, 80},
  {2000, 90, 0}
};

static constexpr PatternStep DISARM_CONFIRM_STEPS[] = {
  {1700, 120, 80},
  {1400, 140, 0}
};

static constexpr PatternStep SINGLE_SHORT_STEPS[] = {
  {1800, 100, 0}
};

static const PatternStep* patternSteps(BeepPattern pattern, size_t& count) {
  switch (pattern) {
    case BeepPattern::ArmConfirm:
      count = sizeof(ARM_CONFIRM_STEPS) / sizeof(ARM_CONFIRM_STEPS[0]);
      return ARM_CONFIRM_STEPS;
    case BeepPattern::DisarmConfirm:
      count = sizeof(DISARM_CONFIRM_STEPS) / sizeof(DISARM_CONFIRM_STEPS[0]);
      return DISARM_CONFIRM_STEPS;
    case BeepPattern::SingleShort:
      count = sizeof(SINGLE_SHORT_STEPS) / sizeof(SINGLE_SHORT_STEPS[0]);
      return SINGLE_SHORT_STEPS;
    case BeepPattern::None:
    default:
      count = 0;
      return nullptr;
  }
}

static void ensureBuzzerInitialized() {
  if (sirenState.buzzerInitialized) {
    return;
  }

  ledcSetup(BUZZER_LEDC_CHANNEL, 2000, BUZZER_LEDC_RESOLUTION);
  ledcAttachPin(BUZZER_PIN, BUZZER_LEDC_CHANNEL);
  sirenState.buzzerInitialized = true;
}

static void resetSirenSweep() {
  sirenState.frequency = SIREN_MIN_FREQ;
  sirenState.rampUp = true;
}

static void clearBeepPattern() {
  sirenState.activePattern = BeepPattern::None;
  sirenState.toneOn = false;
  sirenState.stepIndex = 0;
  sirenState.stepStartedMs = 0;
  sirenState.currentToneFrequency = 0;
  sirenState.currentStepDurationMs = 0;
  sirenState.pendingPauseMs = 0;
}

static void startToneOnBuzzer(int frequency) {
  ensureBuzzerInitialized();
  ledcWriteTone(BUZZER_LEDC_CHANNEL, frequency);
  sirenState.toneOn = true;
}

static void stopToneOnBuzzer() {
  if (!sirenState.buzzerInitialized || !sirenState.toneOn) {
    return;
  }

  ledcWriteTone(BUZZER_LEDC_CHANNEL, 0);
  ledcWrite(BUZZER_LEDC_CHANNEL, 0);
  sirenState.toneOn = false;
}

static void startPatternStep(const PatternStep& step) {
  sirenState.toneOn = true;
  sirenState.stepStartedMs = millis();
  sirenState.currentToneFrequency = step.frequency;
  sirenState.currentStepDurationMs = step.durationMs;
  sirenState.pendingPauseMs = step.pauseAfterMs;
  startToneOnBuzzer(step.frequency);
}

static void queueBeepPattern(BeepPattern pattern) {
  if (systemState.alarmActive && pattern == BeepPattern::ArmConfirm) {
    return;
  }

  clearBeepPattern();
  sirenState.activePattern = pattern;
}

void startAlarm() {
  systemState.alarmActive = true;
  clearBeepPattern();
}

void stopAlarm() {
  systemState.alarmActive = false;
  stopToneOnBuzzer();
  resetSirenSweep();
}

void queueArmConfirmBeep() {
  queueBeepPattern(BeepPattern::ArmConfirm);
}

void queueDisarmConfirmBeep() {
  queueBeepPattern(BeepPattern::DisarmConfirm);
}

void queueSingleShortBeep() {
  queueBeepPattern(BeepPattern::SingleShort);
}

static void updateAlarmSweep() {
  if (!systemState.alarmActive) {
    return;
  }

  const unsigned long now = millis();
  if (now - sirenState.lastSirenStepMs < SIREN_STEP_MS) {
    return;
  }

  sirenState.lastSirenStepMs = now;
  startToneOnBuzzer(sirenState.frequency);

  if (sirenState.rampUp) {
    sirenState.frequency += SIREN_STEP_FREQ;
    if (sirenState.frequency >= SIREN_MAX_FREQ) {
      sirenState.rampUp = false;
    }
  } else {
    sirenState.frequency -= SIREN_STEP_FREQ;
    if (sirenState.frequency <= SIREN_MIN_FREQ) {
      sirenState.rampUp = true;
    }
  }
}

static void updateBeepPattern() {
  if (systemState.alarmActive || sirenState.activePattern == BeepPattern::None) {
    return;
  }

  size_t count = 0;
  const PatternStep* steps = patternSteps(sirenState.activePattern, count);
  if (steps == nullptr || count == 0) {
    clearBeepPattern();
    return;
  }

  if (sirenState.stepIndex >= count) {
    clearBeepPattern();
    stopToneOnBuzzer();
    return;
  }

  const unsigned long now = millis();

  if (!sirenState.toneOn && sirenState.stepStartedMs == 0) {
    startPatternStep(steps[sirenState.stepIndex]);
    return;
  }

  if (sirenState.toneOn) {
    if (now - sirenState.stepStartedMs >= sirenState.currentStepDurationMs) {
      stopToneOnBuzzer();
      sirenState.toneOn = false;
      sirenState.stepStartedMs = now;

      if (sirenState.pendingPauseMs == 0) {
        sirenState.stepIndex++;
        sirenState.stepStartedMs = 0;
      }
    }
    return;
  }

  if (now - sirenState.stepStartedMs >= sirenState.pendingPauseMs) {
    sirenState.stepIndex++;
    sirenState.stepStartedMs = 0;
  }
}

void updateSiren() {
  if (systemState.alarmActive) {
    updateAlarmSweep();
    return;
  }

  updateBeepPattern();
}
