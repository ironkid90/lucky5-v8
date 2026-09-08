package cloudcode.guestbook.backend;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.data.domain.Sort;

@ExtendWith(MockitoExtension.class)
class BackendControllerTest {

    @Mock
    private MessageRepository repository;

    @InjectMocks
    private BackendController controller;

    @Test
    void returnsMessagesInNewestFirstOrder() {
        GuestBookEntry entry = new GuestBookEntry();
        when(repository.findAll(Sort.by(Sort.Direction.DESC, "_id")))
            .thenReturn(List.of(entry));

        List<GuestBookEntry> result = controller.getMessages();

        assertEquals(List.of(entry), result);
        verify(repository).findAll(Sort.by(Sort.Direction.DESC, "_id"));
    }

    @Test
    void addsMessageWithCreationDate() {
        GuestBookEntry entry = new GuestBookEntry();
        long before = System.currentTimeMillis();

        controller.addMessage(entry);

        long after = System.currentTimeMillis();
        assertTrue(entry.getDate() >= before && entry.getDate() <= after);
        verify(repository).save(entry);
    }
}
