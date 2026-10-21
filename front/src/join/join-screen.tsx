import { useForm, useStore } from '@tanstack/react-form';
import { useMutation } from '@tanstack/react-query';
import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { z } from 'zod';
import { issueDevToken, subscribe } from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { useSession } from '@/session/session-provider';
import { Button } from '@/ui/button';
import { Card } from '@/ui/card';
import { visibleError } from '@/ui/form-errors';
import { Notice } from '@/ui/notice';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Text } from '@/ui/text';
import { TextField } from '@/ui/text-field';
import { space } from '@/ui/tokens';
import { generateDemoCpf, hasCpfLength, maskCpf } from './cpf';

// Only the shape is checked here; whether the CPF is valid is the server's call, and its answer is shown as is.
const joinForm = z.object({ cpf: z.string().refine(hasCpfLength, 'Digite os 11 números do CPF.') });

function useJoin() {
  const { signIn } = useSession();
  return useMutation({
    mutationFn: async (cpf: string) => {
      const membership = await subscribe(cpf);
      const issued = await issueDevToken({ role: 'member', membershipId: membership.id });
      await signIn({
        role: 'member',
        token: issued.accessToken,
        expiresAt: issued.expiresAt,
        membershipId: membership.id,
      });
    },
  });
}

// Not security-sensitive: it only invents a demo CPF, which the server then validates like any other.
const randomDigit = () => Math.floor(Math.random() * 10);

export function JoinScreen() {
  const join = useJoin();
  const form = useForm({
    defaultValues: { cpf: '' },
    validators: { onChange: joinForm },
    onSubmit: ({ value }) => join.mutate(value.cpf),
  });
  const submissionAttempts = useStore(form.store, (state) => state.submissionAttempts);

  return (
    <Screen>
      <ScreenHeader
        eyebrow="Adesão"
        title="Entre para o Clube"
        subtitle="R$ 20,00 por mês, que voltam inteiros como crédito para trocar por recompensas."
      />
      <Card>
        <form.Field name="cpf">
          {(field) => (
            <TextField
              label="CPF"
              placeholder="000.000.000-00"
              keyboardType="number-pad"
              maxLength={14}
              value={field.state.value}
              onChangeText={(text) => field.handleChange(maskCpf(text))}
              onBlur={field.handleBlur}
              onSubmitEditing={() => void form.handleSubmit()}
              error={visibleError(field.state.meta, submissionAttempts)}
              hint="O CPF fica cifrado no servidor; o app só mostra a versão mascarada."
            />
          )}
        </form.Field>
        {join.isError && <Notice tone="error" {...describeError(join.error)} />}
        <View style={styles.actions}>
          <Button label="Aderir" icon="check" onPress={() => void form.handleSubmit()} isLoading={join.isPending} />
          <Button
            label="Gerar CPF de teste"
            icon="dice-5-outline"
            variant="secondary"
            onPress={() => form.setFieldValue('cpf', generateDemoCpf(randomDigit))}
            accessibilityHint="Preenche um CPF válido inventado, para testar a adesão"
          />
        </View>
      </Card>
      <View style={styles.back}>
        <Text tone="textMuted">Já tem uma adesão?</Text>
        <Button label="Voltar e entrar" variant="ghost" onPress={() => router.replace('/')} compact />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  actions: { gap: space.sm },
  back: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', flexWrap: 'wrap', gap: space.xs },
});
