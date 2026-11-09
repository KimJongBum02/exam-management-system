#pragma once

// WinSock2는 Windows.h보다 먼저 포함해야 합니다
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX          // std::min / std::max 를 Windows 매크로로 덮어쓰는 것 방지
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <bcrypt.h>
#include <objbase.h>   // CoCreateGuid

// C++ 표준 라이브러리
#include <string>
#include <vector>
#include <map>
#include <memory>
#include <thread>
#include <mutex>
#include <atomic>
#include <functional>
#include <chrono>
#include <stdexcept>
#include <cstring>
#include <cstdint>
#include <algorithm>
#include <sstream>
#include <iomanip>
