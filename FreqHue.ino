#include <FastLED.h>

#define LED_PIN 7
#define NUM_LEDS 60
#define BYTES_PER_STREAM 62
#define COMMAND_BYTE 192
#define COMMAND_MASK 63
#define MAX_MODE 2

#define CLEAR 0

#define DRAW_STATIC_HUE_AUDIO 1
#define DRAW_RAINBOW_HUE_AUDIO 2
#define DRAW_SCROLLING_HUE_AUDIO 3
#define DRAW_STATIC_HUE_WAVEFORM 4
#define DRAW_ROTATING_HUE_WAVEFORM 5
#define DRAW_PEAK_HUE_AUDIO 6

#define SELF_REFRESH 10
#define DRAW_STATIC_HUE_BREATHING 10
#define DRAW_ROTATING_HUE_BREATHING 11
#define DRAW_STATIC_HUE_ROLLING 12
//#define DRAW_RANDOM_HUE_RIPPLES 13

CRGB leds[NUM_LEDS];
byte stream[NUM_LEDS];

byte displayMode = DRAW_RAINBOW_HUE_AUDIO;
byte timeloop = 0;
byte staticHue = 0;
byte rotatingHue = 0;
byte scrollSlow = 1;
byte frequency = 1;
byte brightness = 255;
bool updated = false;
float mix = 0.5f;
byte minimum = 0;
byte speedMult = 0;
float frosting = 0;

void setup() {
  Serial.begin(230400);
  FastLED.addLeds<WS2812, LED_PIN, GRB>(leds, NUM_LEDS);
  delay(50);
}

void loop() {
  int avbl = Serial.available();
  if(avbl > 0) {
    byte head = Serial.peek();
    if(head >= COMMAND_BYTE) {
      processCommand(Serial.read() & COMMAND_MASK);
    } else if(head <= MAX_MODE && displayMode < SELF_REFRESH) {
      if(avbl >= BYTES_PER_STREAM) {
        byte readMode = Serial.read();
        readStream(readMode);
        if(Serial.read() == 1) {
          displayStream(displayMode, 0);
        }
      }
    } else {
      clearStream();
    }
  }

  if(displayMode >= SELF_REFRESH) {
    displayStream(displayMode, 8);
  }
}

void clearStream() {
  while(Serial.available() > 0) {
      Serial.read();
  }
}

void waitForArgCount(byte count) {
  byte timeout = 0;
  while(Serial.available() < count && timeout++ < 255) {
    delay(1);
  }
}

void readStream(byte mode) {
  for(byte i = 0; i < NUM_LEDS; i++) {
    if(mode == 1) {
      stream[i] = (stream[i] + Serial.read()) / 2;
    } else if(mode == 2) {
      stream[i] = Serial.read();
    }
  }
}

void displayStream(byte mode, byte del) {
  if(mode == DRAW_STATIC_HUE_AUDIO) {
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(staticHue, 255 - stream[i] * frosting, stream[i]);
    }
  } else if(mode == DRAW_RAINBOW_HUE_AUDIO) {
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(i * 4, 255 - stream[i] * frosting, stream[i]);
    }
  } else if(mode == DRAW_SCROLLING_HUE_AUDIO) {
    int sum = 0;
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(i * 4 + timeloop, 255 - stream[i] * frosting, stream[i]); //max((stream[i] - 127) * 2, 0)
      sum += stream[i];
    }
    timeloop += (byte) (sum / (scrollSlow * 51.0));
  } else if (mode == DRAW_STATIC_HUE_WAVEFORM) {
    byte next = max((byte) (averageStream() * (1 - mix) + maxStream() * mix), minimum);
    for(byte i = NUM_LEDS - 1; i > 0; i--) {
      leds[i] = leds[i-1];
    }
    leds[0] = CHSV(staticHue, 255 - next, next);
  } else if (mode == DRAW_ROTATING_HUE_WAVEFORM) {
    byte next = max((byte) (averageStream() * (1 - mix) + maxStream() * mix), minimum);
    for(byte i = NUM_LEDS - 1; i > 0; i--) {
      leds[i] = leds[i-1];
    }
    leds[0] = CHSV(staticHue, 255 - next, next);
    staticHue += rotatingHue;
  } else if(mode == DRAW_PEAK_HUE_AUDIO){
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(stream[i], 255 - stream[i] * frosting, stream[i] + 40);
    }
  } else if(mode == DRAW_STATIC_HUE_BREATHING) {
    float p = brightness / 255.0;
    int t = millis() % (10000 / frequency);
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(staticHue, 255, (byte)(p * (127 * sin(6.28f * (t / 10000.0f) * frequency) + 128)));
    }
  } else if(mode == DRAW_ROTATING_HUE_BREATHING) {
    float p = brightness / 255.0;
    int t = millis() % (10000 / frequency);
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CHSV(staticHue, 255, (byte)(p * (127 * sin(6.28f * (t / 10000.0f) * frequency) + 128)));
    }
    byte test = (byte)(p * (127 * sin(6.28f * (t / 10000.0f) * frequency) + 128));
    if(test < 3 && !updated) {
      staticHue += rotatingHue;
      updated = true;
    } else if(test > 3 && updated) {
      updated = false;
    }
  } else if(mode == DRAW_STATIC_HUE_ROLLING) {//they see me rolling, 
    int t = millis() % (6282 / speedMult);
    for(byte i = 0; i < NUM_LEDS; i++) {
      byte lum = (byte) (brightness * (0.5f * sin(6.28f * ((int) i * frequency / (float) NUM_LEDS) + (t * speedMult / 1000.0f)) + 0.5f));
      leds[i] = CHSV(staticHue, 255 - lum, lum);
    }
  }

  if(del > 0) {
    FastLED.delay(del);
  } else {
    FastLED.show();
  }
}

float averageStream() {
  float sum = 0.0f;
  for(byte i = 0; i < NUM_LEDS; i++) {
    sum += stream[i];
  }
  return sum / NUM_LEDS;
}

byte maxStream() {
  byte mx = 0;
  for(byte i = 0; i < NUM_LEDS; i++) {
    if(stream[i] > mx) mx = stream[i];
  }
  return mx;
}

void processCommand(byte command) {
  if(command == CLEAR) {
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = CRGB(0, 0, 0);
    }
    FastLED.show();
    if(displayMode >= SELF_REFRESH) displayMode = DRAW_STATIC_HUE_AUDIO;
  } else if(command == DRAW_STATIC_HUE_AUDIO) {
    waitForArgCount(2);
    displayMode = command;
    staticHue = Serial.read();
    frosting = Serial.read() / 255.0f;
  } else if(command == DRAW_RAINBOW_HUE_AUDIO) {
    waitForArgCount(1);
    displayMode = command;
    frosting = Serial.read() / 255.0f;
  } else if(command == DRAW_SCROLLING_HUE_AUDIO) {
    waitForArgCount(2);
    displayMode = command;
    scrollSlow = Serial.read();
    frosting = Serial.read() / 255.0f;
    if(scrollSlow == 0) scrollSlow = 1;
  } else if(command == DRAW_STATIC_HUE_WAVEFORM) {
    waitForArgCount(3);
    displayMode = command;
    staticHue = Serial.read();
    mix = Serial.read() / 100.0f;
    minimum = Serial.read();
  } else if(command == DRAW_ROTATING_HUE_WAVEFORM) {
    waitForArgCount(3);
    displayMode = command;
    rotatingHue = Serial.read();
    mix = Serial.read() / 100.0f;
    minimum = Serial.read();
  } else if(command == DRAW_PEAK_HUE_AUDIO) {
    waitForArgCount(1);
    displayMode = command;
    frosting = Serial.read() / 255.0f;
  } else if(command == DRAW_STATIC_HUE_BREATHING) {
    waitForArgCount(3);
    displayMode = command;
    staticHue = Serial.read();
    frequency = Serial.read();
    brightness = Serial.read();
    if(frequency < 1) frequency = 1;
  } else if(command == DRAW_ROTATING_HUE_BREATHING) {
    waitForArgCount(3);
    displayMode = command;
    rotatingHue = Serial.read();
    frequency = Serial.read();
    brightness = Serial.read();
    if(frequency < 1) frequency = 1;
  } else if(command == DRAW_STATIC_HUE_ROLLING) {
    waitForArgCount(4);
    displayMode = command;
    staticHue = Serial.read();
    frequency = Serial.read();
    brightness = Serial.read();
    speedMult = Serial.read();
  }
}

/*
 * CRGB auxBuffer[NUM_LEDS];
 * float spreadSpeed = 0.5f;
 * 
else if(mode == DRAW_STATIC_HUE_RIPPLES) {
    for(byte i = 1; i < NUM_LEDS - 1; i++) {
      float lum = 255 * (spreadSpeed * (leds[i-1].getLuma() + leds[i+1].getLuma()) + (1-spreadSpeed) * leds[i].getLuma());
      auxBuffer[i] = CHSV(staticHue, 255 - lum, lum);
      //auxBuffer[i] = blend(leds[i], blend(leds[i-1], leds[i+1], 0.5f), spreadSpeed);
    }
    for(byte i = 0; i < NUM_LEDS; i++) {
      leds[i] = auxBuffer[i];
    }
    if(random(256) < frequency) {
      leds[1 + random(NUM_LEDS - 2)] = CHSV(staticHue, 255, 255);
    }
}

 else if(command == DRAW_STATIC_HUE_RIPPLES) {
    waitForArgCount(4);
    displayMode = command;
    staticHue = Serial.read();
    frequency = Serial.read();
    brightness = Serial.read();
    spreadSpeed = Serial.read() / 100.0f;
    if(frequency < 1) frequency = 1;
  }
 */

