#  Security Monitoring & Incident Correlation System

ESP32-based security monitoring system with event correlation and incident detection.

This project is developed as a diploma work and demonstrates a full-stack IoT security solution:
- embedded firmware
- event streaming
- backend processing
- incident correlation

---

##  Overview

The system collects telemetry from physical security sensors and transforms raw events into meaningful security incidents using time-based correlation logic.

Instead of sending every sensor trigger as a separate alert, the system:
- aggregates events
- analyzes temporal relationships
- classifies incidents
- provides human-readable explanations

---

##  Architecture
ESP32 → MQTT broker → ASP.NET Core backend → PostgreSQL → Telegram bot


---

##  Components

### 🔹 ESP32 Firmware

- arm / disarm system
- door sensor (reed switch)
- motion sensor (PIR)
- tamper detection
- panic button
- LED indication (armed / disarmed)
- buzzer / siren signaling
- debounce filtering
- MQTT publishing (JSON)
- heartbeat + RSSI telemetry
- connection monitoring (Last Will)

---

### 🔹 Backend (ASP.NET Core)

- MQTT event ingestion
- JSON parsing
- PostgreSQL storage (events + incidents)
- duplicate protection
- correlation engine
- incident classification
- REST API

---

##  Incident Correlation Logic

The system uses time-based correlation to convert events into incidents.

### 🔸 Intrusion

- **Suspected**
  - door open OR motion after arming
- **Confirmed**
  - repeated trigger
  - OR door + motion correlation

---

### 🔸 Sabotage

- **Suspected**
  - connection lost
  - OR tamper event
- **Confirmed**
  - tamper + connection loss
  - multiple tamper signals

---

### 🔸 Panic

- immediate confirmed incident
- no correlation required

---

### 🔸 Sensor Anomaly

- high-frequency repeated triggers
- sliding window detection
- sensor-specific thresholds

---

##  Features

- event → incident transformation
- time-window correlation
- duplicate suppression
- noise filtering
- long cooldown for motion sensors
- human-readable incident explanation

---

##  MQTT Topics

- `home/security/events`
- `home/security/status`

---

##  Data Storage

- raw events journal
- incident journal
- correlation metadata

---

##  Planned Features

- Telegram bot notifications
- incident acknowledgment
- web dashboard
- analytics

---

##  Diploma Goal

To design and implement a system that:
- collects security telemetry
- correlates events in time
- detects incidents
- reduces noise
- provides meaningful alerts

---

##  Key Idea

> Not every event is an incident.  
> The system understands context.
