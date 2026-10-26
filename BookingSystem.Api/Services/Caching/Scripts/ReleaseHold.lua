local storeHoldId = redis.call('GET', KEYS[1])
local storeHoldJson = redis.call('GET', KEYS[2])

if redis.call('EXISTS', KEYS[#KEYS]) == 1 then
    return 9
end

if(storeHoldId ~= ARGV[1]) then 
    return 2
end

if(storeHoldJson ~= ARGV[2]) then
    return 3
end

for i = 3, #KEYS - 1 do
    local slot = redis.call('GET', KEYS[i])
    if(slot ~= ARGV[1]) then
        return 6
    end
end


for i = 1, #KEYS - 1 do
    redis.call('DEL', KEYS[i])
end
return 1
