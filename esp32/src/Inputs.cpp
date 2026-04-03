#include "Inputs.h"
#include <Arduino.h>
#include "Config.h"
#include "Events.h"
#include "Siren.h"
#include "State.h"

static void updateIndicators() {
  digitalWrite(RED_LED_PIN, systemState.armed ? HIGH : LOW);
  digitalWrite(GREEN_LED_PIN, systemState.armed ? LOW : HIGH);
}

static void handleArmButtonPress() {
  systemState.armed = !systemState.armed;
  updateIndicators();

  if (systemState.armed) {
    Serial.println("SYSTEM ARMED");
    publishEvent("system", SYSTEM_SENSOR_ID, "armed");
    queueArmConfirmBeep();
    return;
  }

  Serial.println("SYSTEM DISARMED");
  publishEvent("system", SYSTEM_SENSOR_ID, "disarmed");

  const bool wasAlarmActive = systemState.alarmActive;
  stopAlarm();

  if (wasAlarmActive) {
    queueDisarmConfirmBeep();
  } else {
    queueArmConfirmBeep();
  }
}

static void handlePanicButtonPress() {
  Serial.println("PANIC BUTTON PRESSED");
  publishEvent("panic_button", PANIC_SENSOR_ID, "pressed");
  startAlarm();
}

static void handleDoorStableChange(int newState) {
  if (!systemState.armed) {
    return;
  }

  if (newState == LOW) {
    Serial.println("DOOR CLOSED");
    publishEvent("door", DOOR_SENSOR_ID, "closed");
  } else {
    Serial.println("DOOR OPEN");
    publishEvent("door", DOOR_SENSOR_ID, "open");
  }

  startAlarm();
}

static void handleMotionStableChange(int newState) {
  if (!systemState.armed) {
    return;
  }

  if (newState == HIGH) {
    Serial.println("MOTION DETECTED");
    publishEvent("motion", MOTION_SENSOR_ID, "detected");
    startAlarm();
  } 
}

static void handleDoorTamperStableChange(int newState) {
  if (!systemState.armed) {
    return;
  }

  if (newState == LOW) {
    Serial.println("DOOR TAMPER RESTORED");
    publishEvent("door_tamper", DOOR_TAMPER_SENSOR_ID, "restored");
  } else {
    Serial.println("DOOR TAMPER TRIGGERED");
    publishEvent("door_tamper", DOOR_TAMPER_SENSOR_ID, "triggered");
    startAlarm();
  }
}

static void handleMotionTamperStableChange(int newState) {
  if (!systemState.armed) {
    return;
  }

  if (newState == LOW) {
    Serial.println("MOTION TAMPER RESTORED");
    publishEvent("motion_tamper", MOTION_TAMPER_SENSOR_ID, "restored");
  } else {
    Serial.println("MOTION TAMPER TRIGGERED");
    publishEvent("motion_tamper", MOTION_TAMPER_SENSOR_ID, "triggered");
    startAlarm();
  }
}

static void onArmButtonStableChange(int state) {
  if (state == LOW) {
    handleArmButtonPress();
  }
}

static void onPanicButtonStableChange(int state) {
  if (state == LOW) {
    handlePanicButtonPress();
  }
}

static void updateDebouncedInput(DebouncedInput& input) {
  const int rawState = digitalRead(input.pin);
  const unsigned long now = millis();

  if (rawState != input.lastRawState) {
    input.lastRawState = rawState;
    input.lastChangeMs = now;
  }

  if ((now - input.lastChangeMs) >= input.debounceMs && rawState != input.stableState) {
    input.stableState = rawState;
    if (input.onStableChange != nullptr) {
      input.onStableChange(input.stableState);
    }
  }
}

static void configureCallbacks() {
  armButtonInput.onStableChange = onArmButtonStableChange;
  panicButtonInput.onStableChange = onPanicButtonStableChange;
  doorInput.onStableChange = handleDoorStableChange;
  motionInput.onStableChange = handleMotionStableChange;
  doorTamperInput.onStableChange = handleDoorTamperStableChange;
  motionTamperInput.onStableChange = handleMotionTamperStableChange;
}

void configureInputs() {
  pinMode(DOOR_PIN, INPUT_PULLUP);
  pinMode(PIR_PIN, INPUT);
  pinMode(DOOR_TAMPER_PIN, INPUT_PULLUP);
  pinMode(MOTION_TAMPER_PIN, INPUT_PULLUP);
  pinMode(PANIC_BUTTON_PIN, INPUT_PULLUP);
  pinMode(ARM_BUTTON_PIN, INPUT_PULLUP);

  pinMode(RED_LED_PIN, OUTPUT);
  pinMode(GREEN_LED_PIN, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);

  configureCallbacks();
  updateIndicators();
}

void initializeInputStates() {
  armButtonInput.stableState = armButtonInput.lastRawState = digitalRead(armButtonInput.pin);
  panicButtonInput.stableState = panicButtonInput.lastRawState = digitalRead(panicButtonInput.pin);
  doorInput.stableState = doorInput.lastRawState = digitalRead(doorInput.pin);
  motionInput.stableState = motionInput.lastRawState = digitalRead(motionInput.pin);
  doorTamperInput.stableState = doorTamperInput.lastRawState = digitalRead(doorTamperInput.pin);
  motionTamperInput.stableState = motionTamperInput.lastRawState = digitalRead(motionTamperInput.pin);
}

void updateInputs() {
  updateDebouncedInput(armButtonInput);
  updateDebouncedInput(panicButtonInput);
  updateDebouncedInput(doorInput);
  updateDebouncedInput(motionInput);
  updateDebouncedInput(doorTamperInput);
  updateDebouncedInput(motionTamperInput);
}
