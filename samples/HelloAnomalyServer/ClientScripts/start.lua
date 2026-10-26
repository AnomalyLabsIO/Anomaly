Messages.On("GetState", function(Message)
    Messages.Send("State", {
        RequestId = Message.RequestId,
        Enabled = true
    })
end)
