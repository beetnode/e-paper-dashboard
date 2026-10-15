#include "hardware.h"
#include "constants.h"
#include <driver/rtc_io.h>

namespace {
// RTC memory survives deep sleep, so consecutive failed wakes can be counted.
RTC_DATA_ATTR uint8_t failedWakeCount = 0;

uint64_t backoffSeconds(uint8_t failures)
{
  uint64_t seconds = Retry::BackoffBaseSeconds;
  for (uint8_t i = 0; i < failures && seconds < Retry::BackoffMaxSeconds; ++i)
  {
    seconds *= 2;
  }
  return min(seconds, min(Retry::BackoffMaxSeconds, Timing::FallbackRefreshSeconds));
}
}

void Hardware::startDeepSleep(uint64_t waitSeconds)
{
  uint64_t waitMicroseconds = waitSeconds * Timing::SecToUsec;
  esp_sleep_enable_timer_wakeup(waitMicroseconds);
  esp_sleep_enable_ext0_wakeup(Pin::ResetWakeup, 1);
  rtc_gpio_pullup_dis(Pin::ResetWakeup);
  rtc_gpio_pulldown_en(Pin::ResetWakeup);
  esp_deep_sleep_start();
}

void Hardware::sleepAfterFailure()
{
  const uint64_t waitSeconds = backoffSeconds(failedWakeCount);
  if (failedWakeCount < UINT8_MAX)
  {
    ++failedWakeCount;
  }
  Serial.printf("Retrying after failure #%u in %llu seconds\n",
                failedWakeCount, static_cast<unsigned long long>(waitSeconds));
  startDeepSleep(waitSeconds);
}

void Hardware::resetFailureBackoff()
{
  failedWakeCount = 0;
}

bool Hardware::isResetRequested()
{
  pinMode(Pin::ResetWakeup, INPUT_PULLDOWN);
  if (digitalRead(Pin::ResetWakeup) != HIGH)
  {
    return false;
  }

  pinMode(Pin::Led, OUTPUT);
  const unsigned long pressStartTime = millis();
  const unsigned long requiredWaitingTime = Timing::ResetRequestTimeoutSec * 1000;
  unsigned long lastToggle = pressStartTime;
  bool ledState = false;
  bool thresholdReached = false;

  while (digitalRead(Pin::ResetWakeup) == HIGH)
  {
    unsigned long now = millis();
    if (now - pressStartTime >= requiredWaitingTime)
    {
      // Hold threshold reached — keep LED solid ON, wait for release
      if (!thresholdReached)
      {
        thresholdReached = true;
        digitalWrite(Pin::Led, HIGH);
      }
    }
    else if (now - lastToggle >= 1000)
    {
      lastToggle = now;
      ledState = !ledState;
      digitalWrite(Pin::Led, ledState ? HIGH : LOW);
    }
  }

  // Button released
  digitalWrite(Pin::Led, LOW);
  return thresholdReached;
}

void Hardware::resetDevice(ConfigStore& configStore)
{
  configStore.clear();
  delay(1000); // Allow preferences to flush to NVS before restart
  ESP.restart();
}


