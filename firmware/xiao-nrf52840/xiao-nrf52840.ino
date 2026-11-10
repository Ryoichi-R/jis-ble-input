#include <bluefruit.h>
#include <Adafruit_TinyUSB.h>
#include <InternalFileSystem.h>
#include "src/Controller.h"
#include "src/Pairing.h"
#include "src/HidDescriptor.h"

using namespace Adafruit_LittleFS_Namespace;
using namespace jbi;
static volatile bool pairingAllowed = false;
static volatile uint32_t pairingStarted = 0;
static volatile bool registered = false;
static bool storageReady = false;
static ble_gap_addr_t registeredPeer = {};
static volatile uint16_t connectionHandle = BLE_CONN_HANDLE_INVALID;
static volatile bool securedConnection = false;
static volatile bool suspended = false;
static volatile bool capsKnown = false;
static volatile uint8_t leds = 0;
static volatile bool registrationPending = false;
static ble_gap_addr_t pendingPeer = {};
static uint16_t pendingHandle = BLE_CONN_HANDLE_INVALID;
static const char *kPeerFile = "/jbi-peer-v1";
static uint8_t boardId[16] __attribute__((aligned(4))) = {};

// Called by the narrowly patched, workspace-local Seeed security dispatcher.
// Without this hook, Bluefruit automatically accepts new pairing requests.
extern "C" bool jbi_allow_pairing(uint16_t) { return pairingWindowOpen(pairingAllowed, registered, pairingStarted, millis()); }

class JisHid : public BLEHidGeneric {
    static void onControl(uint16_t, BLECharacteristic *, uint8_t *data, uint16_t length) {
        if (length == 1 && data[0] <= 1) suspended = data[0] == 0;
    }
    static void onLed(uint16_t, BLECharacteristic *, uint8_t *data, uint16_t length) {
        if (length == 1) {
            leds = data[0];
            capsKnown = true;
        }
    }

public:
    JisHid() : BLEHidGeneric(1, 1, 0) {}
    err_t begin() {
        static uint16_t inputLength[] = {8};
        static uint16_t outputLength[] = {1};
        enableKeyboard(true);
        enableMouse(false);
        setReportMap(kHidDescriptor, sizeof(kHidDescriptor));
        setReportLen(inputLength, outputLength);
        err_t result = BLEHidGeneric::begin();
        if (result != ERROR_NONE) return result;
        _chr_control.setWriteCallback(onControl, false);
        _chr_outputs[0].setWriteCallback(onLed, false);
        _chr_boot_keyboard_output->setWriteCallback(onLed, false);
        return result;
    }
    bool subscribed(uint16_t handle) {
        return isBootMode() ? _chr_boot_keyboard_input->notifyEnabled(handle) : _chr_inputs[0].notifyEnabled(handle);
    }
    int tryNotify(const uint8_t report[8]) {
        uint16_t handle = connectionHandle;
        if (handle == BLE_CONN_HANDLE_INVALID || !subscribed(handle) || suspended) return -1;
        BLECharacteristic *characteristic = isBootMode() ? _chr_boot_keyboard_input : &_chr_inputs[0];
        uint16_t length = 8;
        ble_gatts_hvx_params_t parameters = {};
        parameters.handle = characteristic->handles().value_handle;
        parameters.type = BLE_GATT_HVX_NOTIFICATION;
        parameters.p_len = &length;
        parameters.p_data = report;
        // Do not call BLECharacteristic::notify: it waits on a semaphore.
        // This HID is the sole notification producer; SoftDevice owns credits.
        uint32_t result = sd_ble_gatts_hvx(handle, &parameters);
        if (result == NRF_ERROR_RESOURCES) return 0;
        if (result == NRF_SUCCESS) {
            characteristic->write(report, 8);
            return 1;
        }
        return -1;
    }
};
static JisHid hid;
static BLEDis deviceInformation;

static void advertise() {
    Bluefruit.Advertising.stop();
    if (!registered && !pairingAllowed) return;
    Bluefruit.Advertising.start(0);
}

class DeviceBackend : public Backend {
    uint8_t outgoing_[2048] = {};
    size_t head_ = 0, count_ = 0;

public:
    bool overflow = false;
    void send(const Frame &frame) {
        if (!Serial) return;
        uint8_t buffer[kMaximumFrame + 2];
        size_t count = encode(frame, buffer);
        if (count > sizeof(outgoing_) - count_) {
            overflow = true;
            return;
        }
        for (size_t i = 0; i < count; ++i) outgoing_[(head_ + count_++) % sizeof(outgoing_)] = buffer[i];
    }
    void flush() {
        if (!Serial) {
            head_ = count_ = 0;
            return;
        }
        while (count_ && Serial.availableForWrite() > 0) {
            Serial.write(outgoing_[head_]);
            head_ = (head_ + 1) % sizeof(outgoing_);
            --count_;
        }
    }
    int notify(const uint8_t report[8]) { return hid.tryNotify(report); }
    bool openPairing() {
        if (registered) return false;
        pairingStarted = millis();
        pairingAllowed = true;
        advertise();
        return true;
    }
    bool clearBond() {
        if (!storageReady || ((registered || InternalFS.exists(kPeerFile)) && !InternalFS.remove(kPeerFile))) return false;
        pairingAllowed = false;
        if (registered) bond_remove_key(BLE_GAP_ROLE_PERIPH, &registeredPeer);
        registered = false;
        memset(&registeredPeer, 0, sizeof(registeredPeer));
        if (connectionHandle != BLE_CONN_HANDLE_INVALID) Bluefruit.disconnect(connectionHandle);
        advertise();
        return true;
    }
};
static DeviceBackend backend;
static Controller controller(backend);
static FrameReader frameReader;
static void onPairComplete(uint16_t handle, uint8_t result);

static void onEvent(ble_evt_t *event) {
    uint16_t handle = event->evt.common_evt.conn_handle;
    if (event->header.evt_id == BLE_GAP_EVT_CONNECTED) {
        connectionHandle = handle;
        suspended = false;
        capsKnown = false;
        securedConnection = false;
        BLEConnection *connection = Bluefruit.Connection(handle);
        if (!pairingAllowed) {
            taskENTER_CRITICAL();
            bool hasRegistration = registered;
            ble_gap_addr_t expectedPeer = registeredPeer;
            taskEXIT_CRITICAL();
            bond_keys_t keys = {};
            bool loaded = hasRegistration && connection && connection->loadBondKey(&keys);
            bool known = knownPeer(hasRegistration, loaded, keys.peer_id.id_addr_info.addr, keys.peer_id.id_addr_info.addr_type,
                                   expectedPeer.addr, expectedPeer.addr_type);
            memset(&keys, 0, sizeof(keys));
            if (!known) Bluefruit.disconnect(handle);
        }
    }
    if (event->header.evt_id == BLE_GAP_EVT_DISCONNECTED) {
        connectionHandle = BLE_CONN_HANDLE_INVALID;
        suspended = false;
        capsKnown = false;
        securedConnection = false;
    }
    if (event->header.evt_id == BLE_GAP_EVT_CONN_SEC_UPDATE) {
        BLEConnection *connection = Bluefruit.Connection(handle);
        securedConnection = connection && connection->secured();
    }
    if (event->header.evt_id == BLE_GAP_EVT_AUTH_STATUS)
        onPairComplete(handle, event->evt.gap_evt.params.auth_status.auth_status);
}

static void onPairComplete(uint16_t handle, uint8_t result) {
    if (result != BLE_GAP_SEC_STATUS_SUCCESS || !pairingAllowed || registered) return;
    BLEConnection *connection = Bluefruit.Connection(handle);
    if (!connection) {
        Bluefruit.disconnect(handle);
        return;
    }
    // Bluefruit saves the bond in its callback task after this event, so the
    // file is usually not readable yet. Transfer only the connection address
    // to the loop, which loads the saved identity once it exists.
    taskENTER_CRITICAL();
    pendingPeer = connection->getPeerAddr();
    pendingHandle = handle;
    registrationPending = true;
    taskEXIT_CRITICAL();
}

static void saveRegistration() {
    if (!registrationPending) return;
    taskENTER_CRITICAL();
    ble_gap_addr_t address = pendingPeer;
    uint16_t handle = pendingHandle;
    taskEXIT_CRITICAL();
    bond_keys_t keys = {};
    bool loaded = bond_load_keys(BLE_GAP_ROLE_PERIPH, &address, &keys);
    ble_gap_addr_t peer = loaded ? keys.peer_id.id_addr_info : address;
    memset(&keys, 0, sizeof(keys));
    RegistrationStep step = registrationStep(pairingWindowOpen(pairingAllowed, registered, pairingStarted, millis()), loaded);
    if (step == RegistrationStep::Wait) return; // deferred bond save still pending; retry next loop
    taskENTER_CRITICAL();
    registrationPending = false;
    taskEXIT_CRITICAL();
    if (step == RegistrationStep::Abort) {
        if (!registered) bond_remove_key(BLE_GAP_ROLE_PERIPH, &peer);
        Bluefruit.disconnect(handle);
        return;
    }
    pairingAllowed = false;
    taskENTER_CRITICAL();
    registeredPeer = peer;
    taskEXIT_CRITICAL();
    File file(InternalFS);
    bool saved = file.open(kPeerFile, FILE_O_WRITE) && file.write(reinterpret_cast<const uint8_t *>(&registeredPeer), sizeof(registeredPeer)) == sizeof(registeredPeer);
    file.close();
    registered = saved;
    if (!saved) {
        bond_remove_key(BLE_GAP_ROLE_PERIPH, &registeredPeer);
        Bluefruit.disconnect(handle);
    }
}

void setup() {
    Serial.begin(115200); // no while(!Serial), no application-triggered 1200bps touch
    TinyUSB_Port_GetSerialNumber(boardId);
    storageReady = InternalFS.begin();
    File file(InternalFS);
    if (storageReady && file.open(kPeerFile, FILE_O_READ)) {
        registered = file.size() == sizeof(registeredPeer) && file.read(reinterpret_cast<uint8_t *>(&registeredPeer), sizeof(registeredPeer)) == sizeof(registeredPeer);
        file.close();
    }
    Bluefruit.begin(1, 0);
    Bluefruit.setEventCallback(onEvent);
    Bluefruit.Security.setIOCaps(false, false, false);
    Bluefruit.Security.setMITM(false); // Just Works, encrypted but not MITM authenticated
    char name[32];
    snprintf(name, sizeof(name), "JIS BLE Keyboard %02X%02X", boardId[6], boardId[7]);
    Bluefruit.setName(name);
    Bluefruit.setAppearance(BLE_APPEARANCE_HID_KEYBOARD);
    Bluefruit.setTxPower(0);
    deviceInformation.setManufacturer("Ryoichi-R");
    deviceInformation.setModel("JIS BLE Input XIAO " JBI_FIRMWARE_VERSION);
    deviceInformation.begin();
    // No Battery Service: this firmware uses USB power, not a measured battery.
    if (hid.begin() != ERROR_NONE) {
        while (true) delay(100);
    }
    Bluefruit.Periph.setConnInterval(9, 12);
    Bluefruit.Advertising.addFlags(BLE_GAP_ADV_FLAGS_LE_ONLY_GENERAL_DISC_MODE);
    Bluefruit.Advertising.addAppearance(BLE_APPEARANCE_HID_KEYBOARD);
    Bluefruit.Advertising.addService(hid);
    Bluefruit.ScanResponse.addName();
    Bluefruit.Advertising.restartOnDisconnect(false);
    Bluefruit.Advertising.setInterval(32, 244);
    controller.identity(boardId, kDescriptorHash);
    advertise();
}

void loop() {
    static uint32_t lastByte = 0;
    static uint16_t previousHandle = BLE_CONN_HANDLE_INVALID;
    uint32_t now = millis();
    saveRegistration();
    if (pairingWindowExpired(pairingAllowed, pairingStarted, now)) {
        pairingAllowed = false;
        if (!registered && connectionHandle != BLE_CONN_HANDLE_INVALID) Bluefruit.disconnect(connectionHandle);
        advertise();
    }
    uint16_t handle = connectionHandle;
    if (handle != previousHandle) {
        previousHandle = handle;
        if (handle == BLE_CONN_HANDLE_INVALID) advertise();
    }
    uint8_t flags = registered ? 64 : 0;
    if (handle != BLE_CONN_HANDLE_INVALID) {
        flags |= 1;
        if (registered && securedConnection) flags |= 2;
        if (hid.subscribed(handle)) flags |= 4;
        if (suspended) flags |= 8;
        if (capsKnown) flags |= 16;
        if (capsKnown && (leds & 2)) flags |= 32;
    }
    uint8_t peer[8] = {};
    if (registered) {
        memcpy(peer, registeredPeer.addr, 6);
        peer[6] = registeredPeer.addr_type;
    }
    controller.setState(flags, hid.isBootMode() ? 0 : 1, peer);
    if (uint32_t(now - lastByte) >= 250) frameReader.expire();
    // Bound one iteration even if a local process continuously writes USB.
    // PING/watchdog, pairing expiry and BLE release must keep making progress.
    for (unsigned received = 0; received < 256 && Serial.available(); ++received) {
        Frame frame = {};
        int result = frameReader.push(uint8_t(Serial.read()), frame);
        lastByte = now;
        if (result > 0) controller.handle(frame, now);
        else if (result < 0) controller.invalidateSession();
    }
    controller.tick(now, bool(Serial));
    if (backend.overflow) {
        controller.invalidateSession();
        backend.overflow = false;
    }
    backend.flush();
    delay(1);
}
