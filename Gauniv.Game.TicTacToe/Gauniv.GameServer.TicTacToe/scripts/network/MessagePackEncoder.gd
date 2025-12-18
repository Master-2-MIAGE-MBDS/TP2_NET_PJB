extends Node

# Encodeur/Décodeur MessagePack pour Godot
# Compatible avec MessagePack-CSharp utilisé par le serveur

# === ENCODAGE ===

static func encode_message(message: Dictionary) -> PackedByteArray:
	return encode_dict(message)

static func encode_dict(dict: Dictionary) -> PackedByteArray:
	var result = PackedByteArray()
	var size = dict.size()
	
	if size <= 15:
		result.append(0x80 | size)  # fixmap
	elif size <= 65535:
		result.append(0xde)  # map 16
		result.append((size >> 8) & 0xFF)
		result.append(size & 0xFF)
	else:
		result.append(0xdf)  # map 32
		result.append_array(_encode_uint32(size))
	
	for key in dict.keys():
		result.append_array(encode_value(key))
		result.append_array(encode_value(dict[key]))
	
	return result

static func encode_value(value) -> PackedByteArray:
	var result = PackedByteArray()
	
	if value == null:
		result.append(0xc0)  # nil
	elif typeof(value) == TYPE_BOOL:
		result.append(0xc3 if value else 0xc2)  # true/false
	elif typeof(value) == TYPE_INT:
		result.append_array(_encode_int(value))
	elif typeof(value) == TYPE_FLOAT:
		result.append_array(_encode_float(value))
	elif typeof(value) == TYPE_STRING:
		result.append_array(_encode_string(value))
	elif typeof(value) == TYPE_DICTIONARY:
		result.append_array(encode_dict(value))
	elif typeof(value) == TYPE_ARRAY:
		result.append_array(_encode_array(value))
	elif typeof(value) == TYPE_PACKED_BYTE_ARRAY:
		result.append_array(_encode_bin(value))
	else:
		print("[MessagePack] Type non supporté: ", typeof(value))
		result.append(0xc0)  # nil par défaut
	
	return result

static func _encode_int(value: int) -> PackedByteArray:
	var result = PackedByteArray()
	
	if value >= 0:
		if value <= 127:
			result.append(value)  # positive fixint
		elif value <= 255:
			result.append(0xcc)  # uint 8
			result.append(value)
		elif value <= 65535:
			result.append(0xcd)  # uint 16
			result.append((value >> 8) & 0xFF)
			result.append(value & 0xFF)
		elif value <= 4294967295:
			result.append(0xce)  # uint 32
			result.append_array(_encode_uint32(value))
		else:
			result.append(0xcf)  # uint 64
			result.append_array(_encode_uint64(value))
	else:
		if value >= -32:
			result.append(0xe0 | (value & 0x1f))  # negative fixint
		elif value >= -128:
			result.append(0xd0)  # int 8
			result.append(value & 0xFF)
		elif value >= -32768:
			result.append(0xd1)  # int 16
			result.append((value >> 8) & 0xFF)
			result.append(value & 0xFF)
		else:
			result.append(0xd2)  # int 32
			result.append_array(_encode_int32(value))
	
	return result

static func _encode_float(value: float) -> PackedByteArray:
	var result = PackedByteArray()
	result.append(0xcb)  # float 64
	var packed = PackedByteArray()
	packed.resize(8)
	packed.encode_double(0, value)
	# MessagePack utilise big-endian, inverser si nécessaire
	for i in range(7, -1, -1):
		result.append(packed[i])
	return result

static func _encode_string(value: String) -> PackedByteArray:
	var result = PackedByteArray()
	var str_bytes = value.to_utf8_buffer()
	var length = str_bytes.size()
	
	if length <= 31:
		result.append(0xa0 | length)  # fixstr
	elif length <= 255:
		result.append(0xd9)  # str 8
		result.append(length)
	elif length <= 65535:
		result.append(0xda)  # str 16
		result.append((length >> 8) & 0xFF)
		result.append(length & 0xFF)
	else:
		result.append(0xdb)  # str 32
		result.append_array(_encode_uint32(length))
	
	result.append_array(str_bytes)
	return result

static func _encode_array(value: Array) -> PackedByteArray:
	var result = PackedByteArray()
	var size = value.size()
	
	if size <= 15:
		result.append(0x90 | size)  # fixarray
	elif size <= 65535:
		result.append(0xdc)  # array 16
		result.append((size >> 8) & 0xFF)
		result.append(size & 0xFF)
	else:
		result.append(0xdd)  # array 32
		result.append_array(_encode_uint32(size))
	
	for item in value:
		result.append_array(encode_value(item))
	
	return result

static func _encode_bin(value: PackedByteArray) -> PackedByteArray:
	var result = PackedByteArray()
	var length = value.size()
	
	if length <= 255:
		result.append(0xc4)  # bin 8
		result.append(length)
	elif length <= 65535:
		result.append(0xc5)  # bin 16
		result.append((length >> 8) & 0xFF)
		result.append(length & 0xFF)
	else:
		result.append(0xc6)  # bin 32
		result.append_array(_encode_uint32(length))
	
	result.append_array(value)
	return result

static func _encode_uint32(value: int) -> PackedByteArray:
	var result = PackedByteArray()
	result.append((value >> 24) & 0xFF)
	result.append((value >> 16) & 0xFF)
	result.append((value >> 8) & 0xFF)
	result.append(value & 0xFF)
	return result

static func _encode_uint64(value: int) -> PackedByteArray:
	var result = PackedByteArray()
	for i in range(7, -1, -1):
		result.append((value >> (i * 8)) & 0xFF)
	return result

static func _encode_int32(value: int) -> PackedByteArray:
	return _encode_uint32(value)

# === DÉCODAGE ===

static func decode_message(data: PackedByteArray):
	var pos = {"index": 0}
	var result = _decode_value(data, pos)
	return result

static func _decode_value(data: PackedByteArray, pos: Dictionary):
	if pos.index >= data.size():
		return null
	
	var byte = data[pos.index]
	pos.index += 1
	
	# positive fixint
	if byte <= 0x7f:
		return byte
	
	# fixmap
	if byte >= 0x80 and byte <= 0x8f:
		return _decode_map(data, pos, byte & 0x0f)
	
	# fixarray
	if byte >= 0x90 and byte <= 0x9f:
		return _decode_array(data, pos, byte & 0x0f)
	
	# fixstr
	if byte >= 0xa0 and byte <= 0xbf:
		return _decode_str(data, pos, byte & 0x1f)
	
	# nil
	if byte == 0xc0:
		return null
	
	# false
	if byte == 0xc2:
		return false
	
	# true
	if byte == 0xc3:
		return true
	
	# bin 8
	if byte == 0xc4:
		var length = data[pos.index]
		pos.index += 1
		return _read_bytes(data, pos, length)
	
	# bin 16
	if byte == 0xc5:
		var length = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return _read_bytes(data, pos, length)
	
	# bin 32
	if byte == 0xc6:
		var length = _read_uint32(data, pos)
		return _read_bytes(data, pos, length)
	
	# float 64
	if byte == 0xcb:
		return _decode_float64(data, pos)
	
	# uint 8
	if byte == 0xcc:
		var val = data[pos.index]
		pos.index += 1
		return val
	
	# uint 16
	if byte == 0xcd:
		var val = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return val
	
	# uint 32
	if byte == 0xce:
		return _read_uint32(data, pos)
	
	# uint 64
	if byte == 0xcf:
		return _read_uint64(data, pos)
	
	# int 8
	if byte == 0xd0:
		var val = data[pos.index]
		pos.index += 1
		return val if val < 128 else val - 256
	
	# int 16
	if byte == 0xd1:
		var val = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return val if val < 32768 else val - 65536
	
	# int 32
	if byte == 0xd2:
		return _read_int32(data, pos)
	
	# str 8
	if byte == 0xd9:
		var length = data[pos.index]
		pos.index += 1
		return _decode_str(data, pos, length)
	
	# str 16
	if byte == 0xda:
		var length = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return _decode_str(data, pos, length)
	
	# str 32
	if byte == 0xdb:
		var length = _read_uint32(data, pos)
		return _decode_str(data, pos, length)
	
	# array 16
	if byte == 0xdc:
		var size = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return _decode_array(data, pos, size)
	
	# array 32
	if byte == 0xdd:
		var size = _read_uint32(data, pos)
		return _decode_array(data, pos, size)
	
	# map 16
	if byte == 0xde:
		var size = (data[pos.index] << 8) | data[pos.index + 1]
		pos.index += 2
		return _decode_map(data, pos, size)
	
	# map 32
	if byte == 0xdf:
		var size = _read_uint32(data, pos)
		return _decode_map(data, pos, size)
	
	# negative fixint
	if byte >= 0xe0:
		return byte - 256
	
	print("[MessagePack] Byte non supporté: 0x", String.num_int64(byte, 16))
	return null

static func _decode_map(data: PackedByteArray, pos: Dictionary, size: int) -> Dictionary:
	var result = {}
	for i in range(size):
		var key = _decode_value(data, pos)
		var value = _decode_value(data, pos)
		result[key] = value
	return result

static func _decode_array(data: PackedByteArray, pos: Dictionary, size: int) -> Array:
	var result = []
	for i in range(size):
		result.append(_decode_value(data, pos))
	return result

static func _decode_str(data: PackedByteArray, pos: Dictionary, length: int) -> String:
	var bytes = _read_bytes(data, pos, length)
	return bytes.get_string_from_utf8()

static func _decode_float64(data: PackedByteArray, pos: Dictionary) -> float:
	var bytes = PackedByteArray()
	bytes.resize(8)
	for i in range(8):
		bytes[7 - i] = data[pos.index + i]
	pos.index += 8
	return bytes.decode_double(0)

static func _read_bytes(data: PackedByteArray, pos: Dictionary, length: int) -> PackedByteArray:
	var result = data.slice(pos.index, pos.index + length)
	pos.index += length
	return result

static func _read_uint32(data: PackedByteArray, pos: Dictionary) -> int:
	var val = (data[pos.index] << 24) | (data[pos.index + 1] << 16) | (data[pos.index + 2] << 8) | data[pos.index + 3]
	pos.index += 4
	return val

static func _read_uint64(data: PackedByteArray, pos: Dictionary) -> int:
	var val = 0
	for i in range(8):
		val = (val << 8) | data[pos.index + i]
	pos.index += 8
	return val

static func _read_int32(data: PackedByteArray, pos: Dictionary) -> int:
	var val = _read_uint32(data, pos)
	return val if val < 2147483648 else val - 4294967296

