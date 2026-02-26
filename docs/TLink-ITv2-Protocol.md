# TLink and ITv2 Protocol Documentation

## Overview

This document describes the TLink transport protocol and the ITv2 session/messaging protocol that runs on top of it. This documentation has been compiled from reverse engineering, publicly available tool documentation, and various other sources. As such, some naming conventions and behavioral details have been inferred.

## Protocol Stack

```
┌─────────────────────────────┐
│   ITv2 Session Protocol     │  (Session management, encryption, messaging)
├─────────────────────────────┤
│   TLink Transport Protocol  │  (Framing, byte stuffing)
├─────────────────────────────┤
│   Transport Layer           │  (TCP, Serial, etc.)
└─────────────────────────────┘
```

---

## TLink Protocol (Transport Layer)

### Purpose

TLink provides a simple, transport-agnostic framing protocol. It can operate over TCP, serial connections, and potentially other transports.

### Packet Structure

A TLink packet consists of two fields:

```
┌──────────────┬──────┬──────────────┬──────┐
│    Header    │ 0x7E │   Payload    │ 0x7F │
└──────────────┴──────┴──────────────┴──────┘
```

- **Header**: Variable-length field containing header information
- **Header Delimiter**: `0x7E` byte marks the end of the header
- **Payload**: Variable-length field containing the actual message data
- **Payload Delimiter**: `0x7F` byte marks the end of the payload (and packet)

### Byte Stuffing

To allow the delimiter bytes to appear in the header and payload data, TLink employs a byte stuffing scheme:

| Original Byte | Encoded Sequence |
|--------------|------------------|
| `0x7D`       | `0x7D 0x00`     |
| `0x7E`       | `0x7D 0x01`     |
| `0x7F`       | `0x7D 0x02`     |

**Rules:**
- All occurrences of `0x7D`, `0x7E`, and `0x7F` in the header and payload **must** be stuffed before transmission
- After stuffing, the bytes `0x7E` and `0x7F` must not appear in the encoded header or payload (their presence indicates a framing error)
- The delimiter bytes (`0x7E` and `0x7F`) are **not** stuffed when used as delimiters

### Encoding Process

1. Encode the header bytes using byte stuffing
2. Append `0x7E` delimiter
3. Encode the payload bytes using byte stuffing
4. Append `0x7F` delimiter
5. Transmit the complete packet

### Decoding Process

1. Read bytes until `0x7F` is encountered (packet terminator)
2. Split the packet at `0x7E` to separate header and payload
3. Decode (unstuff) the header sequence
4. Decode (unstuff) the payload sequence

### Example

**Original Data:**
- Header: `0x01 0x02 0x7E 0x03`
- Payload: `0x7D 0x7F 0x04`

**Encoded Packet:**
```
0x01 0x02 0x7D 0x01 0x03 0x7E 0x7D 0x00 0x7D 0x02 0x04 0x7F
└─────────────────────────┘ │  └──────────────────────────┘ │
        Header              │           Payload             │
                            │                               │
                    Header Delimiter              Payload Delimiter
```

### Error Conditions

- **Framing Error**: Missing `0x7E` or `0x7F` delimiter
- **Encoding Error**: Invalid byte stuffing sequence (e.g., `0x7D` followed by value other than `0x00`, `0x01`, or `0x02`)
- **Encoding Error**: Delimiter bytes (`0x7E` or `0x7F`) found in decoded data

### DLSProNet Extension

TLink has an extension called DLSProNet that employs symmetric cipher encryption. This extension is not covered in this document.

---

## ITv2 Protocol (Session/Messaging Layer)

### Purpose

ITv2 provides session management, optional encryption, sequencing, command/response messaging, and notifications on top of the TLink transport.

### Session States

The ITv2 session progresses through the following states:

```
WaitingForOpenSession → WaitingForRequestAccess → Connected → Closed
```

### Transport Encapsulation

The entire ITv2 packet is encoded into the TLink payload field. The layering is:

```
┌───────────────────────────────────────────────┐
│              TLink Header                     │
├───────────────────────────────────────────────┤
│              (TLink Payload)                  │
│  ┌─────────────────────────────────────────┐  │
│  │  ECB Block Cipher (optional)            │  │
│  │  ┌───────────────────────────────────┐  │  │
│  │  │  ITv2 Framing                     │  │  │
│  │  │  ┌─────────────────────────────┐  │  │  │
│  │  │  │  Raw ITv2 Packet            │  │  │  │
│  │  │  └─────────────────────────────┘  │  │  │
│  │  └───────────────────────────────────┘  │  │
│  └─────────────────────────────────────────┘  │
└───────────────────────────────────────────────┘
```

**Processing Order:**
- **Receiving**: TLink framing → ECB decrypt (if enabled) → ITv2 framing → Raw ITv2 packet
- **Sending**: Raw ITv2 packet → ITv2 framing → ECB encrypt (if enabled) → TLink framing

### ITv2 Framing

After optional decryption, the payload uses a length-prefixed framing scheme with CRC protection.

#### Length Field Encoding

The length field can be 1 or 2 bytes:

**Single-byte length** (values 0-127):
```
┌─────────┬───────────────────┬───────┐
│ Length  │   Data (n bytes)  │  CRC  │
│ (1 byte)│                   │(2 byte│
│ bit7=0  │                   │       │
└─────────┴───────────────────┴───────┘
```

**Two-byte length** (values 128-32767):
```
┌──────────────┬───────────────────┬───────┐
│   Length     │   Data (n bytes)  │  CRC  │
│  (2 bytes)   │                   │(2 byte│
│ bit7=1       │                   │       │
└──────────────┴───────────────────┴───────┘
```

**Length Field Rules:**
- If **bit 7 of first byte is clear (0)**: Length is 1 byte, value is 0-127
- If **bit 7 of first byte is set (1)**: Length is 2 bytes (big-endian 15-bit word)
  - Discard bit 7 of the first byte when interpreting the length
  - Maximum length: 32767 bytes

**Example:**
- `0x05` → Length = 5 bytes
- `0x80 0x10` → Length = 16 bytes (0x8010 & 0x7FFF = 0x0010)
- `0xFF 0xFF` → Length = 32767 bytes (0xFFFF & 0x7FFF = 0x7FFF)

#### CRC Field

The last 2 bytes of the length-encoded field contain a CRC-16 checksum.

**CRC Algorithm: CRC-16/XMODEM (CRC-16/CCITT-FALSE)**

- **Polynomial**: `0x1021` (x^16 + x^12 + x^5 + 1)
- **Initial Value**: `0xFFFF`
- **XOR Out**: None (0x0000)
- **Reflect Input**: No
- **Reflect Output**: No

**CRC Calculation:**
1. **Input**: Length byte(s) + Data bytes (excluding the 2 CRC bytes themselves)
2. **Process**: 
   - Initialize CRC to `0xFFFF`
   - For each byte: `CRC = (CRC << 8) ^ lookupTable[(CRC >> 8) ^ byte]`
3. **Output**: 16-bit CRC value
4. **Byte Order**: Big-endian (high byte first, then low byte)

**Example:**
```
Data: [Length bytes] + [Message bytes]
CRC: Calculated over all the above
Final Frame: [Length] [Message] [CRC_High] [CRC_Low]
```

**Validation:**
On receive, recalculate the CRC over the length and message bytes, then compare with the received CRC bytes. If they don't match, the frame is corrupted.

**Implementation**: See `ITv2Framing.cs` static method `crc16(IEnumerable<byte> crcRange)`

#### Block Cipher Padding

When the optional ECB block cipher is active:
- The total encrypted payload length may exceed the encoded length field
- Padding is added to align to the cipher block size
- The length field indicates the **actual data length** (before padding)
- Excess bytes after the length-encoded field should be discarded

### Raw ITv2 Packet Format

After removing the framing (length prefix and CRC), the remaining bytes form the raw ITv2 packet.

#### SimpleAck Message (Minimal/Empty Message)

The simplest message contains only the two sequence bytes and no additional data:

```
┌──────────────┬───────────────┐
│ SenderSeq    │ ReceiverSeq   │
│  (1 byte)    │  (1 byte)     │
└──────────────┴───────────────┘
```

- Used as a protocol-level acknowledgment
- Contains no message type or data
- Essentially a "null" or "empty" message

#### Standard Messages

All messages that contain data include a message type field:

```
┌──────────────┬───────────────┬─────────────────┬─────────────────┐
│ SenderSeq    │ ReceiverSeq   │  Message Type   │  Message Data   │
│  (1 byte)    │  (1 byte)     │  (2 bytes)      │  (0-N bytes)    │
└──────────────┴───────────────┴─────────────────┴─────────────────┘
```

- **SenderSequence** (1 byte): Sequence number from the sender (increments with each packet)
- **ReceiverSequence** (1 byte): Acknowledgment of the last received sequence from remote peer
- **Message Type** (2 bytes): Identifies the message type (always present if packet contains data)
  - Big-endian word
- **Message Data** (0-N bytes): Message-specific payload (may be empty for some message types)

#### Command Request Messages

Messages classified as "Command Request" or "Command" messages have a CommandSequence byte immediately after the message type:

```
┌──────────────┬───────────────┬─────────────────┬─────────────────┬─────────────────┐
│ SenderSeq    │ ReceiverSeq   │  Message Type   │  CommandSeq     │  Message Data   │
│  (1 byte)    │  (1 byte)     │  (2 bytes)      │  (1 byte)       │  (0-N bytes)    │
└──────────────┴───────────────┴─────────────────┴─────────────────┴─────────────────┘
```

- **CommandSequence** (1 byte): Correlation ID for matching requests with responses
  - Considered part of the message data
  - Common to all command request messages
  - Increments independently from sender/receiver sequences

### Sequence Numbers

Sequence numbers are incrementally rolling byte values (0-255, wrap at overflow). Because the protocol operates between exactly two nodes, the terms "sender/receiver" and "host/remote" are relative to perspective.

#### Terminology

- **Local Node**: The implementation being described (yourself)
- **Remote Node**: The other end of the connection
- **Sender**: The node transmitting a message (in the context of that message)
- **Receiver**: The node receiving a message (in the context of that message)
- **Local Sequence**: Internal counter maintained by each node, incremented per transaction

**Note**: Each node is simultaneously "host" and "remote" depending on perspective. When messages arrive, you are the receiver; when sending, you are the sender.

#### Initial Values

- **Local Sequence**: Starts at `1`
- **Remote Sequence**: Starts at `0` (last known sender sequence from remote node)

#### Transaction-Based Sequencing

**Every message is part of a transaction.** Every transaction is completed with a `SimpleAck` reply.

The local sequence counter is incremented **once per transaction initiated by the local node**, and that sequence number is used for **all messages** in that transaction.

### Notification Messages (Simple Transactions)

Notification messages follow a simple two-message pattern:

1. **Notification sent** → 2. **SimpleAck reply** (transaction complete)

#### Sending a Notification (Outbound Transaction)

Example: Sending a `ConnectionPoll` (heartbeat) message

1. **Increment** the local sequence counter: `localSequence++`
2. **Create packet**:
   - `SenderSequence` = new local sequence value
   - `ReceiverSequence` = last known sender sequence from remote node
   - `Message` = notification data (e.g., `ConnectionPoll`)
3. **Send** the message
4. **Wait** for `SimpleAck` reply
5. **Validate** reply: `SimpleAck.ReceiverSequence` must equal the `SenderSequence` from step 2

#### Receiving a Notification (Inbound Transaction)

Example: Receiving a notification from remote node

1. **Receive** notification message
2. **Record** the remote sequence: `remoteSequence = inboundMessage.SenderSequence`
3. **Send** `SimpleAck` reply:
   - `SenderSequence` = current local sequence (no increment for replies)
   - `ReceiverSequence` = `inboundMessage.SenderSequence` (acknowledging receipt)

**Important**: When replying with `SimpleAck`, the local sequence is **not incremented** because the transaction was initiated by the remote node, not locally.

### Command Request/Response Messages (Command-Level Transactions)

Command Request messages are distinguished by the presence of a **CommandSequence byte** in the message data. Command-level transactions introduce a second layer of correlation on top of protocol-level transactions.

#### Command Sequence

- **Shared Counter**: The command sequence is a shared value between **both nodes**
- **Incremented**: With each command-level transaction, **regardless of which node initiates it**
- **Correlation**: Used to match command requests with their responses
- **Independent**: Increments independently from sender/receiver sequences

#### Command Transaction Patterns

Command-level transactions can occur in two ways:

1. **Within a single protocol-level transaction** (synchronous)
2. **Spanning two protocol-level transactions** (asynchronous)

---

#### Pattern A: Synchronous Command Response (Single Protocol Transaction)

In this pattern, the command response arrives immediately within the same protocol-level transaction.

**Message Flow:**

```
Local Node                          Remote Node
    |                                    |
    |  1. CommandRequest                 |
    |    SenderSeq: X                    |
    |    ReceiverSeq: R                  |
    |    CommandSeq: Y                   |
    |----------------------------------->|
    |                                    |
    |  2. CommandResponse                |
    |    SenderSeq: R (unchanged)        |
    |    ReceiverSeq: X (ack step 1)     |
    |    CommandSeq: Y (same)            |
    |<-----------------------------------|
    |                                    |
    |  3. SimpleAck                      |
    |    SenderSeq: X (unchanged)        |
    |    ReceiverSeq: R (ack step 2)     |
    |----------------------------------->|
    |                                    |
```

**Steps:**

1. **Send CommandRequest**:
   - Increment local sequence → `localSequence = X`
   - Increment command sequence → `commandSequence = Y`
   - Set `SenderSequence = X`, `ReceiverSequence = R`, `CommandSequence = Y`
   - Send message

2. **Receive CommandResponse**:
   - **Same protocol transaction**: `SenderSequence` and `ReceiverSequence` unchanged from remote's perspective
   - `CommandSequence = Y` (matches request)
   - Command transaction is now complete (response received)

3. **Send SimpleAck**:
   - `SenderSequence = X` (same as step 1, no increment for replies within transaction)
   - `ReceiverSequence` = sender sequence from step 2
   - Closes **both** protocol-level and command-level transactions

**Characteristics:**
- Command response arrives before protocol transaction closes
- All messages use the **same** sender/receiver sequences
- Code refers to this as **"synchronous command response"**

---

#### Pattern B: Asynchronous Command Response (Spanning Two Protocol Transactions)

In this pattern, the remote node acknowledges receipt immediately with `SimpleAck`, then sends the actual command response later in a **separate protocol transaction**.

**Message Flow:**

```
Local Node                          Remote Node
    |                                    |
    |  1. CommandRequest                 |
    |    SenderSeq: X                    |
    |    ReceiverSeq: R                  |
    |    CommandSeq: Y                   |
    |----------------------------------->|
    |                                    |
    |  2. SimpleAck                      |
    |    SenderSeq: R                    |
    |    ReceiverSeq: X                  |
    |<-----------------------------------|
    |                                    |
    |  (Protocol transaction X closed)   |
    |  (Command transaction Y pending)   |
    |                                    |
    |  ... time passes ...               |
    |                                    |
    |  3. CommandResponse (new protocol) |
    |    SenderSeq: R+n (NEW)            |
    |    ReceiverSeq: X+m (NEW)          |
    |    CommandSeq: Y (same)            |
    |<-----------------------------------|
    |                                    |
    |  4. SimpleAck                      |
    |    SenderSeq: current local        |
    |    ReceiverSeq: R+n                |
    |----------------------------------->|
    |                                    |
```

**Steps:**

1. **Send CommandRequest**:
   - Increment local sequence → `localSequence = X`
   - Increment command sequence → `commandSequence = Y`
   - Set `SenderSequence = X`, `ReceiverSequence = R`, `CommandSequence = Y`
   - Send message

2. **Receive SimpleAck**:
   - Closes the **protocol-level transaction X**
   - Command-level transaction Y remains **pending** (waiting for response)

3. **Receive CommandResponse** (later, in a new protocol transaction):
   - **Different protocol transaction**: Sender/receiver sequences are **new** (may be different from step 1)
   - `CommandSequence = Y` (matches original request)
   - Match pending command transaction using `CommandSequence`
   - Command transaction is now complete

4. **Send SimpleAck**:
   - Uses current local sequence (may have incremented since step 1)
   - Closes the **new protocol transaction** and the **command transaction Y**

**Characteristics:**
- Command response arrives in a **separate** protocol transaction
- Sender/receiver sequences are **different** from the original request
- Command sequence is used to correlate the response with the original request
- Code refers to this as **"asynchronous command response"** or **"async response"**

---

#### Receiving Command Requests

When receiving a command request from the remote node:

1. **Receive CommandRequest**
2. **Update** command sequence: `commandSequence = inboundMessage.CommandSequence`
3. **Process** the command
4. **Choose** response pattern:
   - **Synchronous**: Send `CommandResponse` immediately (within same protocol transaction)
   - **Asynchronous**: Send `SimpleAck` immediately, send `CommandResponse` later (new protocol transaction)

**For Synchronous:**
- Send `CommandResponse` with same sender/receiver sequences
- Wait for `SimpleAck` to close both transactions

**For Asynchronous:**
- Send `SimpleAck` to close current protocol transaction
- Later: Initiate new protocol transaction with `CommandResponse` (using current sequences)
- Wait for `SimpleAck` to close both new protocol transaction and command transaction

---

### Multiple Message Packet (Multi Messages)

The protocol supports **consolidated messages** called "Multiple Message Packets" or "Multi Messages." These are notification messages that encapsulate multiple sub-messages within a single protocol-level transaction.

#### Structure

```
┌──────────────────────────────────────────┐
│  Multiple Message Packet (Notification)  │
│  ┌────────────────────────────────────┐  │
│  │  Sub-Message 1 (Notification)      │  │
│  ├────────────────────────────────────┤  │
│  │  Sub-Message 2 (Notification)      │  │
│  ├────────────────────────────────────┤  │
│  │  Sub-Message 3 (Command Response)  │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

#### Content Rules

- **Multiple Notifications**: Can contain several notification messages
- **One Command Response**: May contain **at most one** command response message (I have only ever seen one command message, so I assume thats the rule)
- **Mixed**: Can contain a mix of notifications and one command response
- **Never**: Multiple command messages have never been observed in a single multi message

#### Transaction Behavior

The multi message itself is treated as a **notification** at the protocol level:

1. **Receive** Multi Message (notification)
2. **Extract** sub-messages
3. **Process** each sub-message:
   - **Notifications**: Yield to application
   - **Command Response**: If it correlates to a pending command transaction:
     - Closes the **command transaction** (no further response needed)
     - Does **not** affect the protocol transaction
4. **Send** `SimpleAck` to close the **protocol-level transaction**

#### Important Notes

- The protocol transaction containing the multi message is **separate** from any command transaction completed by an embedded command response
- A command response embedded in a multi message **completes the command transaction** but does **not** generate its own `SimpleAck`
- The multi message as a whole (being a notification) requires a single `SimpleAck` response regardless of its contents

#### Example: Multi Message with Command Response

```
Local Node                          Remote Node
    |                                    |
    |  (Earlier: Sent CommandRequest    |
    |   CommandSeq: Y, waiting...)       |
    |                                    |
    |  1. Multi Message (Notification)   |
    |     SenderSeq: R+1                 |
    |     ReceiverSeq: X+m               |
    |     Contains:                      |
    |       - Notification A             |
    |       - CommandResponse (Seq: Y)   | ← Closes command transaction Y
    |       - Notification B             |
    |<-----------------------------------|
    |                                    |
    |  2. SimpleAck                      |
    |     SenderSeq: current local       |
    |     ReceiverSeq: R+1               | ← Closes protocol transaction
    |----------------------------------->|
    |                                    |
```

**Result:**
- Command transaction Y: **Closed** (by embedded command response)
- Protocol transaction: **Closed** (by SimpleAck in step 2)

### Message Types

ITv2 supports several message categories:

#### Command Messages
- Implement `ICommandMessageData`
- Include `CorrelationID` field for request/response matching
- May have `AsyncResponse` flag
- Examples: `OpenSession`, `RequestAccess`, `CommandRequest`, `CommandResponse`

#### Notifications
- Asynchronous events from the panel
- Always acknowledged with `SimpleAck`
- Examples: `NotificationPartitionReadyStatus`, `NotificationArmDisarm`, `ModuleZoneStatus`

#### Protocol Messages
- `SimpleAck`: Protocol-level acknowledgment
- `MultipleMessagePacket`: Container for batching multiple messages
- `ConnectionPoll`: Heartbeat/keepalive message

### Session Establishment

Session establishment follows a specific handshake sequence involving `OpenSession` and `RequestAccess` command messages. The handshake negotiates encryption and completes before the session enters the `Connected` state.

#### Handshake Sequence

**State Flow:**
```
Uninitialized → WaitingForOpenSession → WaitingForRequestAccess → Connected
```

**Message Exchange:**

```
Server Node (You)              Alarm Panel (Remote)
      |                               |
      |  1. OpenSession               |
      |     (unencrypted)             |
      |     EncryptionType: X         |
      |<------------------------------|
      |                               |
      |  2. CommandResponse           |
      |     (unencrypted)             |
      |------------------------------>|
      |                               |
      |  3. SimpleAck                 |
      |     (unencrypted)             |
      |<------------------------------|
      |                               |
      |  4. OpenSession               |
      |     (unencrypted)             |
      |     EncryptionType: X (same)  |
      |------------------------------>|
      |                               |
      |  5. CommandResponse           |
      |     (unencrypted)             |
      |<------------------------------|
      |                               |
      |  6. SimpleAck                 |
      |     (unencrypted)             |
      |------------------------------>|
      |                               |
      |  7. RequestAccess             |
      |     (unencrypted)             |
      |     Initializer: [bytes]      |
      |<------------------------------|
      |                               |
      | ┌─────────────────────────┐   |
      | │ Enable OUTBOUND encrypt │   |
      | └─────────────────────────┘   |
      |                               |
      |  8. CommandResponse           |
      |     (encrypted)               |
      |------------------------------>|
      |                               |
      |  9. SimpleAck                 |
      |     (plaintext)               |
      |<------------------------------|
      |                               |
      | 10. RequestAccess             |
      |     (encrypted)               |
      |     Initializer: [bytes]      |
      |------------------------------>|
      |                               |
      |                         ┌─────────────────────────┐
      |                         │ Enable INBOUND encrypt  │
      |                         └─────────────────────────┘
      |                               |
      | 11. CommandResponse           |
      |     (encrypted)               |
      |<------------------------------|
      |                               |
      | 12. SimpleAck                 |
      |     (encrypted)               |
      |------------------------------>|
      |                               |
      | *** Session Connected ***     |
```

#### Step-by-Step Details

**Steps 1-3: Alarm Panel → Server OpenSession**

1. **Receive OpenSession** (unencrypted):
   - Contains `EncryptionType` (Type1 or Type2)
   - Contains session parameters
   - Synchronous command transaction
2. **Send CommandResponse** (unencrypted)
3. **Receive SimpleAck** (unencrypted)
   - Transaction complete

**Steps 4-6: Server → Alarm Panel OpenSession**

4. **Send OpenSession** (unencrypted):
   - **Must** use the same `EncryptionType` received in step 1
   - Other parameters: typically mirror what was received (echoing back works)
   - Synchronous command transaction
5. **Receive CommandResponse** (unencrypted)
6. **Send SimpleAck** (unencrypted)
   - Transaction complete
   - Encryption handler is now instantiated but not yet active

**Steps 7-9: Alarm Panel → Server RequestAccess**

7. **Receive RequestAccess** (unencrypted):
   - Contains `Initializer` field (byte array)
   - Use initializer to **immediately configure outbound encryption**
   - **All subsequent outbound messages are now encrypted**
8. **Send CommandResponse** (**encrypted**):
   - First encrypted message sent
9. **Receive SimpleAck** (plaintext):
   - Transaction complete

**Steps 10-12: Server → Alarm Panel RequestAccess**

10. **Send RequestAccess** (encrypted):
    - Contains `Initializer` field generated by local encryption handler
    - Alarm panel uses this to configure **its outbound encryption** (our inbound)
11. **Receive CommandResponse** (encrypted):
    - **All subsequent inbound messages are now encrypted**
    - Use local initializer to decrypt
12. **Send SimpleAck** (encrypted)
    - Transaction complete
    - **Session is now fully connected**

#### Important Notes

- **Only the initial handshake is unencrypted**; all other communication is encrypted
- **Encryption type must match** between the two `OpenSession` messages
- **Asymmetric activation**: Outbound encryption activates after receiving first `RequestAccess`, inbound encryption activates after sending second `RequestAccess`
- **Echo strategy**: Responding with the same `OpenSession` parameters received has been observed to work
- **Immediate application**: Encryption applies immediately after configuration, including within the same command transaction

### Command/Response Pattern

**TODO: To be documented based on upcoming details**

### Notification Handling

**TODO: To be documented based on upcoming details**

### Encryption

ITv2 uses ECB (Electronic Codebook) block cipher encryption for message payloads. Encryption is negotiated during session establishment and is active for nearly all communication after the initial handshake.

#### Encryption Types

Two encryption types are supported:

- **Type 1**: Uses a simpler key derivation method
  - Access code configured in panel: `[851][423, 450, 477, 504]` (Type 1 Access Code)
- **Type 2**: Uses a more robust key generation scheme
  - Access code configured in panel: `[851][700, 701, 702, 703]` (Type 2 Access Code)

**Key Differences:**
- The encryption type determines how encryption keys are derived/generated
- **Type 2 is more secure** due to enhanced key generation
- Once established, the **choice of type does not affect message functionality**
- Both types use the same underlying cipher algorithm (ECB)

#### Encryption Lifecycle

1. **Before Handshake**: All messages unencrypted
2. **During Handshake** (after first `RequestAccess`):
   - Outbound encryption enabled
   - Inbound still unencrypted
3. **During Handshake** (after second `RequestAccess`):
   - Both outbound and inbound encrypted
4. **Connected State**: All messages encrypted

#### Initializer Field

The `RequestAccess.Initializer` field contains initialization data for the encryption algorithm:

- **Received Initializer**: Used to configure **outbound** encryption (encrypting data you send)
- **Generated Initializer**: Sent to remote node to configure **inbound** encryption (decrypting data you receive)
- Applied immediately upon receipt/send

#### Implementation

Encryption details are implemented in:
- `Type1EncryptionHandler` class
- `Type2EncryptionHandler` class
- Both inherit from `EncryptionHandler` base class

Methods:
- `ConfigureOutboundEncryption(byte[] initializer)`: Set up encryption for sending
- `ConfigureInboundEncryption()`: Generate initializer and set up decryption for receiving
- `HandleOutboundData(byte[] plaintext)`: Encrypt before sending
- `HandleInboundData(byte[] ciphertext)`: Decrypt after receiving

#### Troubleshooting

If encryption negotiation fails during `RequestAccess`:
- **Type 1**: Verify the Type 1 Access Code `[851][423, 450, 477, 504]` is correct
- **Type 2**: Verify the Type 2 Access Code `[851][700, 701, 702, 703]` is correct
- Ensure the alarm panel's configured encryption type matches what is expected

### Heartbeat and Timeouts

**Connection Timeout:**
- The alarm panel will **close the connection** if **120 seconds** elapse without receiving any message
- This applies to any message activity (notifications, commands, SimpleAck, etc.)

**Heartbeat Mechanism:**
- To maintain the connection, send a `ConnectionPoll` message periodically
- **Recommended interval**: **100 seconds**
  - This provides a 20-second safety margin before the 120-second timeout
- The `ConnectionPoll` is a notification message and follows the standard protocol transaction pattern (send → receive SimpleAck)

**Implementation:**
```csharp
// From ITv2Session.cs beginHeartBeat method:
do
{
    await Task.Delay(TimeSpan.FromSeconds(100), cancellation);
    await SendAsync(new ConnectionPoll(), cancellation);
    _logger.LogDebug("Sent Heartbeat");
} while (!cancellation.IsCancellationRequested);
```

**Other Timeouts:**
- Queue flush timeout: **2 seconds** before allowing outbound commands after reconnection
- Transaction lock timeout: **30 seconds** to prevent deadlocks

### Error Handling: Two-Level Error Response Architecture

ITv2 provides two distinct error response mechanisms that correspond to the physical architecture of the system:

#### Physical Architecture Context

The DSC PowerSeries NEO system consists of two components:
1. **Alarm Panel**: The main controller that manages security functions
2. **Communicator Module**: An add-on card connected to the alarm panel via serial link
   - Establishes TCP/IP connections to remote servers
   - Handles the ITv2 protocol and encryption
   - Relays messages between remote servers and the alarm panel

#### Error Response Types

**1. CommandError (NACK Response)**

Sent by the **communicator module** when protocol or communication-level errors occur:

- **CRC/Framing Errors**: Invalid message framing or checksum failures
- **Sequence Errors**: Incorrect sender/receiver sequence numbers
- **Malformed Messages**: Messages that don't conform to protocol structure
- **Protocol Violations**: Invalid state transitions, missing required fields

**When this occurs:**
- The message never reaches the alarm panel
- The communicator rejects it at the protocol layer
- Response: `CommandError` with NACK code indicating the protocol error

**2. CommandResponse (Response Code)**

Sent by the **alarm panel** (relayed through the communicator) when the command is valid at the protocol level but fails at the application level:

- **Authentication Failures**: Incorrect user codes, access denied
- **Invalid Operations**: Command not allowed in current panel state
- **Business Logic Errors**: Invalid zone numbers, out-of-range values

**When this occurs:**
- The communicator validates the message format
- The message is forwarded to the alarm panel via serial link
- The alarm panel processes the command and generates a response
- Response: `CommandResponse` with response code (Success, InternalError, etc.)

#### Example: Disarm with Incorrect Code

```
Remote Server                Communicator              Alarm Panel
     |                            |                         |
     | Disarm (wrong code)        |                         |
     |--------------------------->|                         |
     |                            | ✓ Valid protocol        |
     |                            | Forward message         |
     |                            |------------------------>|
     |                            |                         |
     |                            |    ✗ Invalid code       |
     |                            |    Generate error       |
     |                            |<------------------------|
     |                            |                         |
     | CommandResponse            |                         |
     | ResponseCode: Error        |                         |
     |<---------------------------|                         |
```

**Summary:**
- **Protocol/Communication Errors** → Handled by communicator → `CommandError` (NACK)
- **Application/Business Logic Errors** → Handled by alarm panel → `CommandResponse` (Response Code)

This two-tier architecture allows the communicator to filter out malformed traffic without burdening the alarm panel, while still allowing the panel to enforce its own security and business rules.

### Error Codes

Common error codes used in ITv2:

| Code | Meaning |
|------|---------|
| `Disconnected` | Transport closed or session shut down |
| `Timeout` | No response within expected timeout |
| `Unknown` | Unspecified error condition |

---

## Implementation Notes

### Message Receivers

The implementation tracks pending messages using `MessageReceiver` objects:
- **CommandReceiver**: Waits for command response with matching `CorrelationID`
- **NotificationReceiver**: Waits for protocol-level `SimpleAck`

### Concurrency

- `_sendLock`: Ensures serialized sending of outbound packets
- `_queueFlush`: Prevents sending until initial receive queue is drained
- Independent send and receive paths

### Message Pump

The session uses a continuous message pump (`GetNotificationsAsync`) that:
1. Reads incoming packets
2. Routes command responses to pending receivers
3. Acknowledges notifications
4. Yields notifications to the application
5. Handles `MultipleMessagePacket` expansion

---

## Errata and Known Issues

### Reconnection Behavior - Queued Message Blast

**Observed Behavior:**

After an established session is disconnected and later reconnects, the alarm panel exhibits the following behavior:

1. **Message Queuing**: The alarm panel queues all notifications that occurred during the disconnection period
2. **Message Blast**: Upon reconnection, all queued notifications are sent immediately in rapid succession
3. **Pre-set Sequences**: These queued messages have their sequence numbers already determined (based on when they were queued)
4. **Unresponsive to Inbound Messages**: During the message blast, the alarm panel does not respond to or acknowledge messages sent from the server node

**Impact:**

- Attempting to send messages during the blast may result in:
  - Messages being ignored
  - Sequence number mismatches
  - Transaction timeouts
  - Unpredictable protocol behavior

**Recommended Workaround:**

Implement a **"receive queue flush"** mechanism:

1. After reconnection is established, monitor incoming message activity
2. **Wait for quiet period**: Do not send any outbound messages until the connection has been quiet (no incoming messages) for **1-2 seconds**
3. Only after the quiet period, begin sending outbound traffic

**Implementation Example:**

```csharp
// From ITv2Session.cs ListenAsync method:
Timer? flushQueueTimer = new Timer(_ => 
{
    _log.LogInformation("Receive queue is flushed. Ready to start sending");
    flushQueueTCS.SetResult();  // Unblock sending
    flushQueueTimer = null;
    beginHeartBeat(linkedToken);
});

// Reset timer with each incoming message
flushQueueTimer?.Change(2000, Timeout.Infinite);
```

This approach ensures the alarm panel has finished replaying its queued notifications before the server attempts to send new messages, avoiding sequence conflicts and protocol errors.

---

## Wire Format Examples

**TODO: Add actual hex traces of message exchanges**

---

## References

- Implementation: `DSC.TLink` namespace
- TLink: `TLinkClient.cs`
- ITv2 Session: `ITv2Session.cs`
- Message Definitions: `DSC.TLink.ITv2.Messages` namespace

---

*This documentation is based on reverse engineering, publicly available tool documentation, and observed protocol behavior. As it is not from official sources, some details may be inferred or incomplete.*
