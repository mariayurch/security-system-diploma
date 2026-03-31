# Security Monitoring System (Diploma)

ESP32-based security monitoring system for a diploma project.

The firmware collects security-related sensor events, applies primary debounce filtering, and publishes telemetry via MQTT.  
Further event correlation, incident formation, journaling, and Telegram notifications are planned for the backend side.

## Current functionality

- arm / disarm mode
- door sensor events
- motion sensor (PIR) events
- tamper events
- panic button handling
- local buzzer / siren signaling
- MQTT event publishing in JSON format
- boot event
- heartbeat event
- RSSI telemetry
- connection monitoring via MQTT Last Will

## Architecture

ESP32 → MQTT → Backend → Telegram bot

## Planned backend logic

- event journaling
- event correlation in time windows
- incident formation
- incident classification:
  - intrusion
  - sabotage
  - sensor anomaly
- Telegram notifications

## Tech stack

- ESP32
- C++ / Arduino framework
- PlatformIO
- MQTT
- HiveMQ (used as test broker)
- Backend (planned: C# / ASP.NET Core)
- Telegram bot (planned: C#)

## Project structure

```text
esp32/
├── include/
│   ├── Config.h
│   ├── Events.h
│   ├── Inputs.h
│   ├── Models.h
│   ├── Siren.h
│   ├── State.h
│   ├── WifiMqtt.h
│   └── secrets.h
├── src/
│   ├── Events.cpp
│   ├── Inputs.cpp
│   ├── main.cpp
│   ├── Siren.cpp
│   ├── State.cpp
│   └── WifiMqtt.cpp
├── .gitignore
└── platformio.ini
