#pragma once

#include <Arduino.h>
#include "config_store.h"

class Hardware {
public:
  void startDeepSleep(uint64_t waitSeconds);
  // Sleeps for an exponentially growing interval so a temporarily unavailable
  // WiFi or dashboard service is retried soon instead of after the regular refresh.
  void sleepAfterFailure();
  void resetFailureBackoff();
  bool isResetRequested();
  void resetDevice(ConfigStore& configStore);
};
